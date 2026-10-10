//go:build linux

package main

import (
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"strconv"
	"strings"
	"syscall"
	"time"
)

const hermesDirHint = "Ubuntu"

const hermesBinary = "HermesProxy"

func platformDefaults() map[string]string {
	return map[string]string{
		"proton": filepath.Join(os.Getenv("HOME"), ".steam/steam/steamapps/common/Proton - Experimental/proton"),
	}
}

// wtfSettings: the portal (HermesProxy on this PC) plus Proton stability settings. With DirectX 12
// the client picks ray-traced shadows on capable GPUs, and under vkd3d-proton that froze the game
// right after entering the world; DirectX 11 (DXVK) without RT shadows is stable.
func wtfSettings() map[string]string {
	return map[string]string{"portal": "127.0.0.1", "gxApi": "D3D11", "shadowRt": "0"}
}

func checkPlatform(cfg map[string]string) error {
	if _, err := os.Stat(cfg["proton"]); err != nil {
		return fmt.Errorf("Proton not found at %s (set proton = ... in wotlk343.ini)", cfg["proton"])
	}
	return nil
}

// startGame runs the Burralis launcher under Proton with its own prefix next to this binary.
func startGame(cfg map[string]string, game string) error {
	prefix := filepath.Join(base, "prefix")
	os.MkdirAll(prefix, 0o755)
	run := exec.Command(cfg["proton"], "run", filepath.Join(game, launcherExe), "--version", "Classic", "--staticseed")
	run.Dir = game
	run.Env = append(os.Environ(),
		"STEAM_COMPAT_DATA_PATH="+prefix,
		"STEAM_COMPAT_CLIENT_INSTALL_PATH="+filepath.Join(os.Getenv("HOME"), ".steam/steam"))
	run.Stdout, run.Stderr = os.Stdout, os.Stderr
	return run.Run()
}

// gameRunning reports whether this folder's WowClassic.exe is running (Wine shows it as Z:\...).
func gameRunning(game string) bool {
	want := strings.ToLower(strings.ReplaceAll(filepath.Join(game, "WowClassic.exe"), "/", "\\"))
	entries, _ := os.ReadDir("/proc")
	for _, e := range entries {
		cmd, err := os.ReadFile("/proc/" + e.Name() + "/cmdline")
		if err != nil {
			continue
		}
		if strings.Contains(strings.ToLower(strings.ReplaceAll(string(cmd), "/", "\\")), want) {
			return true
		}
	}
	return false
}

func detach(cmd *exec.Cmd) { cmd.SysProcAttr = &syscall.SysProcAttr{Setpgid: true} }

func stop(cmd *exec.Cmd) { syscall.Kill(-cmd.Process.Pid, syscall.SIGTERM) }

func errorDialog(msg string) {
	if _, err := exec.LookPath("kdialog"); err == nil && os.Getenv("DISPLAY")+os.Getenv("WAYLAND_DISPLAY") != "" {
		exec.Command("kdialog", "--title", "WotLK 3.4.3", "--error", msg).Run()
	}
}

// hermesCert: HermesProxy keeps its built-in TrinityCore certificate; Burralis skips the client's
// certificate checks.
func hermesCert() string { return "" }

// checkGame: the Windows client plus the Burralis launcher next to it.
func checkGame(game string) error {
	if _, err := os.Stat(filepath.Join(game, "WowClassic.exe")); err != nil {
		return fmt.Errorf("WowClassic.exe not found in %s", game)
	}
	if _, err := os.Stat(filepath.Join(game, launcherExe)); err != nil {
		return fmt.Errorf("%q not found next to WowClassic.exe", launcherExe)
	}
	return nil
}

// waitGame: Burralis returns once the game is up; wait for WowClassic.exe to appear, then to exit.
func waitGame(game string) {
	for i := 0; i < 30 && !gameRunning(game); i++ {
		time.Sleep(time.Second)
	}
	for gameRunning(game) {
		time.Sleep(3 * time.Second)
	}
}

// startHotReload opens a terminal running HermesProxy's scripts/hot-reload.sh from repo against the
// installed proxy's folder; dotnet watch asks there before a restart that would drop the game. The
// terminal is detached so the proxy outlives this launcher, and held open if the script stops.
func startHotReload(repo, installDir string) error {
	term, err := exec.LookPath("konsole")
	if err != nil {
		return fmt.Errorf("hotreload opens the proxy in konsole, which was not found: %v", err)
	}
	cmd := exec.Command(term, "--separate", "--hold", "-e", filepath.Join(repo, "scripts", "hot-reload.sh"), installDir)
	detach(cmd)
	return cmd.Start()
}

// stopInstalledProxies stops every process running the installed binary at hermes and waits up to
// 20 s for them to exit. It reports whether there were any.
func stopInstalledProxies(hermes string) bool {
	var pids []int
	entries, _ := os.ReadDir("/proc")
	for _, e := range entries {
		pid, err := strconv.Atoi(e.Name())
		if err != nil {
			continue
		}
		exe, err := os.Readlink("/proc/" + e.Name() + "/exe")
		// A binary replaced while it ran (a newer build installed) reads "<path> (deleted)".
		if err == nil && strings.TrimSuffix(exe, " (deleted)") == hermes {
			syscall.Kill(pid, syscall.SIGTERM)
			pids = append(pids, pid)
		}
	}
	for deadline := time.Now().Add(20 * time.Second); time.Now().Before(deadline); time.Sleep(250 * time.Millisecond) {
		alive := false
		for _, pid := range pids {
			if syscall.Kill(pid, 0) == nil {
				alive = true
			}
		}
		if !alive {
			break
		}
	}
	return len(pids) > 0
}
