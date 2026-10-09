//go:build darwin

package main

import (
	"crypto/sha256"
	"encoding/hex"
	"encoding/pem"
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"syscall"
)

const hermesDirHint = "MacOS"

const hermesBinary = "HermesProxy"

const (
	macApp    = "World of Warcraft Classic.app"
	macExe    = macApp + "/Contents/MacOS/World of Warcraft Classic"
	macDir    = "mac" // wotlk343hook.dylib, localhost.crt, localhost.pfx
	hookLib   = "wotlk343hook.dylib"
	hookCert  = "localhost.crt"
	hookPfx   = "localhost.pfx"
	codesign  = "/usr/bin/codesign"
	xattrTool = "/usr/bin/xattr"
)

func platformDefaults() map[string]string { return map[string]string{} }

// wtfSettings: with the portal suffix blanked (patchMacClient) the portal CVar is the whole host name.
// The Mac client matches it against the certificate's common name (CN=localhost), so not 127.0.0.1.
func wtfSettings() map[string]string { return map[string]string{"portal": "localhost"} }

// hermesCert: HermesProxy serves the localhost certificate that the hook dylib pins.
func hermesCert() string { return filepath.Join(base, macDir, hookPfx) }

// checkPlatform makes the folder runnable on this Mac: clears the download quarantine, restores
// execute bits a copy may have lost, patches the client once and (re)signs anything whose
// signature is not valid (Apple Silicon only runs signed code; ad hoc is enough).
func checkPlatform(map[string]string) error {
	for _, f := range []string{hookLib, hookCert, hookPfx} {
		if _, err := os.Stat(filepath.Join(base, macDir, f)); err != nil {
			return fmt.Errorf("%s not found in %s", f, filepath.Join(base, macDir))
		}
	}
	exec.Command(xattrTool, "-dr", "com.apple.quarantine", base).Run()
	game := filepath.Join(base, filepath.FromSlash(gameDir))
	app := filepath.Join(game, macApp)
	filepath.WalkDir(app, func(p string, d os.DirEntry, err error) error {
		if err == nil && !d.IsDir() && strings.Contains(p, "/Contents/MacOS/") {
			os.Chmod(p, 0o755)
		}
		return nil
	})
	if hermes := findHermes(filepath.Join(base, "hermes")); hermes != "" {
		os.Chmod(hermes, 0o755)
		ensureSigned(hermes)
	}
	patched, err := patchMacClient(filepath.Join(game, filepath.FromSlash(macExe)))
	if err != nil {
		return fmt.Errorf("patching the Mac client: %v", err)
	}
	if patched || !signatureValid(app) {
		if out, err := exec.Command(codesign, "--force", "--sign", "-", app).CombinedOutput(); err != nil {
			return fmt.Errorf("signing the patched client: %v\n%s", err, out)
		}
	}
	return ensureSigned(filepath.Join(base, macDir, hookLib))
}

func signatureValid(path string) bool { return exec.Command(codesign, "--verify", path).Run() == nil }

func ensureSigned(path string) error {
	if signatureValid(path) {
		return nil
	}
	if out, err := exec.Command(codesign, "--force", "--sign", "-", path).CombinedOutput(); err != nil {
		return fmt.Errorf("signing %s: %v\n%s", filepath.Base(path), err, out)
	}
	return nil
}

// checkGame: the Mac client app.
func checkGame(game string) error {
	if _, err := os.Stat(filepath.Join(game, filepath.FromSlash(macExe))); err != nil {
		return fmt.Errorf("%s not found in %s", macApp, game)
	}
	return nil
}

// certPin is the SHA-256 of the localhost certificate HermesProxy serves.
func certPin() (string, error) {
	raw, err := os.ReadFile(filepath.Join(base, macDir, hookCert))
	if err != nil {
		return "", err
	}
	block, _ := pem.Decode(raw)
	if block == nil {
		return "", fmt.Errorf("%s is not a PEM certificate", hookCert)
	}
	sum := sha256.Sum256(block.Bytes)
	return hex.EncodeToString(sum[:]), nil
}

// startGame runs the Mac client with the trust hook injected and waits for it to exit. The hook
// accepts HermesProxy's localhost certificate (pinned by hash) where Burralis would skip the
// client's certificate checks; its log is mac-hook.log next to this launcher.
func startGame(_ map[string]string, game string) error {
	pin, err := certPin()
	if err != nil {
		return err
	}
	run := exec.Command(filepath.Join(game, filepath.FromSlash(macExe)))
	run.Dir = game
	run.Env = append(os.Environ(),
		"DYLD_INSERT_LIBRARIES="+filepath.Join(base, macDir, hookLib),
		"WOTLK343_PIN="+pin,
		"WOTLK343_HOOKLOG="+filepath.Join(base, "mac-hook.log"))
	if logf, err := os.Create(filepath.Join(base, "mac-game.log")); err == nil {
		defer logf.Close()
		run.Stdout, run.Stderr = logf, logf
	}
	return run.Run()
}

// waitGame: startGame already waited for the client to exit.
func waitGame(string) {}

// gameRunning reports whether this folder's Mac client is running.
func gameRunning(game string) bool {
	return exec.Command("/usr/bin/pgrep", "-f", filepath.Join(game, filepath.FromSlash(macExe))).Run() == nil
}

func detach(cmd *exec.Cmd) { cmd.SysProcAttr = &syscall.SysProcAttr{Setpgid: true} }

func stop(cmd *exec.Cmd) { syscall.Kill(-cmd.Process.Pid, syscall.SIGTERM) }

func errorDialog(msg string) {
	esc := strings.NewReplacer(`\`, `\\`, `"`, `\"`).Replace(msg)
	exec.Command("/usr/bin/osascript", "-e",
		`display dialog "`+esc+`" with title "WotLK 3.4.3" buttons {"OK"} default button 1 with icon stop`).Run()
}
