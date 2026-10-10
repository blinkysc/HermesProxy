// WotLK343 — one-click launcher for the WoW Classic 3.4.3.54261 client on a 3.3.5a AzerothCore server.
// Builds for Linux (client under Proton), Windows (client run directly) and macOS (native Mac client,
// patched on first run); platform parts live in platform_linux.go / platform_windows.go / platform_darwin.go.
//
// Put the binary in the folder that holds everything:
//
//	WoW_3.4.3.54261/_classic_/WowClassic.exe   (+ "Burralis Game Launcher.exe" next to it)
//	hermes/HermesProxy-<OS>-.../HermesProxy[.exe] (HermesProxy builds; the one for this OS is used)
//	mac/            (macOS only: trust hook dylib + localhost certificate for HermesProxy)
//	mirror/tpr/...  (optional: local CDN mirror, only used if the client wants to fetch something)
//	wotlk343.ini    (written with defaults on first run: server address, mirror, Proton path)
//
// It writes the client's portal setting and HermesProxy's server address from wotlk343.ini, serves
// the mirror on 127.0.0.1:8765 (the install's .build.info points its CDN there), starts HermesProxy,
// starts the client through the Burralis launcher, waits for WowClassic.exe to exit, then stops
// everything it started.
package main

import (
	"bufio"
	"encoding/json"
	"fmt"
	"net"
	"net/http"
	"os"
	"os/exec"
	"path/filepath"
	"regexp"
	"strings"
	"time"
)

const (
	gameDir     = "WoW_3.4.3.54261/_classic_"
	launcherExe = "Burralis Game Launcher.exe"
	mirrorAddr  = "127.0.0.1:8765"
	bnetAddr    = "127.0.0.1:1119" // HermesProxy's BNet listener (appsettings ProxyNetworkOptions.BNetPort)
	proxyMarker = "hermes.running" // present while a proxy this launcher started is running
	// Burralis --staticseed pins the client's auth seed to this value (its Commandline Usage.txt)
	staticSeed = "91D59BB7D4E183A5222B5F38F4B886FF"
)

var base string

func fail(format string, a ...any) {
	msg := fmt.Sprintf(format, a...)
	fmt.Fprintln(os.Stderr, "WotLK343:", msg)
	errorDialog(msg)
	os.Exit(1)
}

func readIni(path string) map[string]string {
	cfg := map[string]string{
		"server": "192.168.0.140",
		"port":   "3724",
		"mirror": "mirror",
	}
	for k, v := range platformDefaults() {
		cfg[k] = v
	}
	f, err := os.Open(path)
	if err != nil {
		var b strings.Builder
		b.WriteString("# WotLK343 launcher settings\n")
		for _, k := range []string{"server", "port", "mirror", "proton"} {
			if v, ok := cfg[k]; ok {
				fmt.Fprintf(&b, "%s = %s\n", k, v)
			}
		}
		os.WriteFile(path, []byte(b.String()), 0o644)
		return cfg
	}
	defer f.Close()
	sc := bufio.NewScanner(f)
	for sc.Scan() {
		line := strings.TrimSpace(sc.Text())
		if line == "" || strings.HasPrefix(line, "#") {
			continue
		}
		if k, v, ok := strings.Cut(line, "="); ok {
			cfg[strings.TrimSpace(k)] = strings.TrimSpace(v)
		}
	}
	return cfg
}

// setWtf sets (or adds) `SET key "value"` lines in Config.wtf.
func setWtf(path string, kv map[string]string) error {
	data, _ := os.ReadFile(path)
	lines := []string{}
	for _, l := range strings.Split(strings.ReplaceAll(string(data), "\r\n", "\n"), "\n") {
		keep := l != ""
		for k := range kv {
			if strings.HasPrefix(strings.ToLower(l), "set "+strings.ToLower(k)+" ") {
				keep = false
			}
		}
		if keep {
			lines = append(lines, l)
		}
	}
	for k, v := range kv {
		lines = append(lines, fmt.Sprintf("SET %s \"%s\"", k, v))
	}
	os.MkdirAll(filepath.Dir(path), 0o755)
	return os.WriteFile(path, []byte(strings.Join(lines, "\r\n")+"\r\n"), 0o644)
}

// configureHermes points HermesProxy at the legacy server and the Burralis static seed, and sets the
// BNet TLS certificate (empty cert = HermesProxy's built-in TrinityCore certificate).
func configureHermes(path, server, port, cert string) error {
	raw, err := os.ReadFile(path)
	if err != nil {
		return err
	}
	raw = regexp.MustCompile(`(?m)^\s*//.*$`).ReplaceAll(raw, nil)
	var cfg map[string]any
	if err := json.Unmarshal(raw, &cfg); err != nil {
		return err
	}
	section := func(name string) map[string]any {
		m, ok := cfg[name].(map[string]any)
		if !ok {
			m = map[string]any{}
			cfg[name] = m
		}
		return m
	}
	var p int
	fmt.Sscan(port, &p)
	client := section("ClientOptions")
	client["ClientBuild"] = "V3_4_3_54261"
	client["SeedHex"] = staticSeed
	legacy := section("LegacyServerOptions")
	legacy["Build"] = "V3_3_5a_12340"
	legacy["Address"] = server
	legacy["Port"] = p
	network := section("ProxyNetworkOptions")
	network["ExternalAddress"] = "127.0.0.1"
	if cert != "" {
		network["CertificatePfxPath"] = cert
	} else {
		delete(network, "CertificatePfxPath")
	}
	out, _ := json.MarshalIndent(cfg, "", "  ")
	return os.WriteFile(path, out, 0o644)
}

// findHermes returns the HermesProxy binary for this OS: the Linux and macOS builds share the file
// name, so prefer the one whose path names this platform (hermesDirHint).
func findHermes(root string) string {
	var found []string
	filepath.WalkDir(root, func(p string, d os.DirEntry, err error) error {
		if err == nil && !d.IsDir() && strings.EqualFold(d.Name(), hermesBinary) {
			found = append(found, p)
		}
		return nil
	})
	for _, p := range found {
		if strings.Contains(strings.ToLower(p), strings.ToLower(hermesDirHint)) {
			return p
		}
	}
	if len(found) > 0 {
		return found[0]
	}
	return ""
}

func main() {
	exe, _ := os.Executable()
	base, _ = filepath.Abs(filepath.Dir(exe))
	if strings.HasSuffix(base, filepath.Join(".app", "Contents", "MacOS")) {
		base = filepath.Dir(filepath.Dir(filepath.Dir(base))) // WotLK343.app on macOS: use the folder holding the .app
	}
	cfg := readIni(filepath.Join(base, "wotlk343.ini"))

	game := filepath.Join(base, filepath.FromSlash(gameDir))
	if err := checkGame(game); err != nil {
		fail("%v", err)
	}
	hermes := findHermes(filepath.Join(base, "hermes"))
	if hermes == "" {
		fail("%s not found under %s", hermesBinary, filepath.Join(base, "hermes"))
	}
	if err := checkPlatform(cfg); err != nil {
		fail("%v", err)
	}
	if gameRunning(game) {
		fail("the 3.4.3 client is already running")
	}

	if err := setWtf(filepath.Join(game, "WTF", "Config.wtf"), wtfSettings()); err != nil {
		fail("writing Config.wtf: %v", err)
	}
	if err := configureHermes(filepath.Join(filepath.Dir(hermes), "appsettings.json"), cfg["server"], cfg["port"], hermesCert()); err != nil {
		fail("configuring HermesProxy: %v", err)
	}

	// local CDN mirror: Go's file server answers Range requests, which the client uses for archives
	mirror := cfg["mirror"]
	if !filepath.IsAbs(mirror) {
		mirror = filepath.Join(base, mirror)
	}
	if st, err := os.Stat(filepath.Join(mirror, "tpr")); err == nil && st.IsDir() {
		go http.ListenAndServe(mirrorAddr, http.FileServer(http.Dir(mirror)))
	}

	var proxy *exec.Cmd
	var exited chan struct{}
	if repo := cfg["hotreload"]; repo != "" {
		useHotReloadProxy(expandHome(repo), hermes)
	} else if externalProxyRunning() {
		// A proxy started on its own (HermesProxy's scripts/hot-reload.sh, which runs it from
		// source) already owns the port: use it, and leave it running when the game exits.
		fmt.Println("WotLK343: using the HermesProxy already listening on", bnetAddr)
	} else {
		proxy, exited = startHermes(hermes)
	}

	if err := startGame(cfg, game); err != nil {
		fmt.Fprintln(os.Stderr, "launcher:", err)
	}

	waitGame(game) // keep the proxy (and mirror) alive until the game exits
	if proxy != nil {
		stop(proxy)
		<-exited
	}
}

// useHotReloadProxy makes the game talk to HermesProxy run from source under dotnet watch, which
// patches fixes into the running proxy (hotreload = <HermesProxy repo> in wotlk343.ini). One left
// running by an earlier launch is reused; the proxy is never stopped when the game exits.
func useHotReloadProxy(repo, hermes string) {
	// The installed binary on the same ports would shadow the hot-reload proxy, and hot-reload.sh
	// refuses to start while any HermesProxy runs.
	if stopInstalledProxies(hermes) {
		fmt.Println("WotLK343: stopped the installed HermesProxy for hot-reload mode")
	}
	if proxyListening() {
		fmt.Println("WotLK343: using the hot-reload HermesProxy already listening on", bnetAddr)
		return
	}
	if err := startHotReload(repo, filepath.Dir(hermes)); err != nil {
		fail("starting the hot-reload HermesProxy: %v", err)
	}
	// The first start builds the proxy from source.
	for deadline := time.Now().Add(3 * time.Minute); time.Now().Before(deadline); time.Sleep(time.Second) {
		if proxyListening() {
			return
		}
	}
	fail("the hot-reload HermesProxy was not listening on %s after 3 minutes; see its terminal window", bnetAddr)
}

// expandHome turns a leading ~/ into the home directory, as a shell would.
func expandHome(path string) string {
	if rest, ok := strings.CutPrefix(path, "~/"); ok {
		return filepath.Join(os.Getenv("HOME"), rest)
	}
	return path
}

// proxyListening reports whether something already accepts connections on HermesProxy's BNet port.
func proxyListening() bool {
	conn, err := net.DialTimeout("tcp", bnetAddr, 500*time.Millisecond)
	if err != nil {
		return false
	}
	conn.Close()
	return true
}

// externalProxyRunning reports whether a HermesProxy this launcher should use rather than start is
// already listening. A launcher marks the proxy it starts with proxyMarker until that proxy exits,
// so a listener with the marker present is a previous launcher's proxy still shutting down (the
// game was restarted within a few seconds): wait for it to go, then start our own.
func externalProxyRunning() bool {
	if !proxyListening() {
		return false
	}
	marker := filepath.Join(base, proxyMarker)
	for deadline := time.Now().Add(20 * time.Second); time.Now().Before(deadline); time.Sleep(250 * time.Millisecond) {
		if _, err := os.Stat(marker); os.IsNotExist(err) {
			break
		}
	}
	// Marker gone, or left behind by a launcher that was killed: trust the port either way.
	return proxyListening()
}

// startHermes starts the HermesProxy binary with its output in hermes.log; exited closes when it ends.
func startHermes(hermes string) (*exec.Cmd, chan struct{}) {
	logf, _ := os.Create(filepath.Join(base, "hermes.log"))
	proxy := exec.Command(hermes)
	proxy.Dir = filepath.Dir(hermes)
	proxy.Stdout, proxy.Stderr = logf, logf
	detach(proxy)
	if err := proxy.Start(); err != nil {
		fail("starting HermesProxy: %v", err)
	}
	marker := filepath.Join(base, proxyMarker)
	os.WriteFile(marker, []byte(fmt.Sprintln(proxy.Process.Pid)), 0o644)
	exited := make(chan struct{})
	go func() { proxy.Wait(); os.Remove(marker); close(exited) }()
	select {
	case <-exited:
		fail("HermesProxy exited at start, see %s", filepath.Join(base, "hermes.log"))
	case <-time.After(2 * time.Second):
	}
	return proxy, exited
}
