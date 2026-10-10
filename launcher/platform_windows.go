//go:build windows

package main

import (
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"syscall"
	"time"
	"unsafe"
)

const hermesDirHint = "Windows"

const hermesBinary = "HermesProxy.exe"

const createNoWindow = 0x08000000 // keep HermesProxy's console hidden; its output goes to hermes.log

var (
	user32     = syscall.NewLazyDLL("user32.dll")
	messageBox = user32.NewProc("MessageBoxW")
)

func platformDefaults() map[string]string { return map[string]string{} }

// wtfSettings: just the portal (HermesProxy on this PC); DirectX 12 runs natively on Windows.
func wtfSettings() map[string]string { return map[string]string{"portal": "127.0.0.1"} }

// checkPlatform refuses to run under Wine/Proton: the Windows HermesProxy dies there (Wine's
// bcrypt has no RC2, which .NET needs to read its PKCS#12 certificate). This happens when a Steam
// shortcut to the Linux launcher has a forced compatibility tool: Wine can't run the ELF binary and
// falls back to WotLK343.exe next to it.
func checkPlatform(map[string]string) error {
	ntdll := syscall.NewLazyDLL("ntdll.dll")
	if ntdll.NewProc("wine_get_version").Find() == nil {
		return fmt.Errorf("this is the Windows launcher running under Proton/Wine, which the Windows " +
			"HermesProxy can't run in.\n\nOn Linux run the native WotLK343 instead. In Steam: shortcut " +
			"Properties > Compatibility > untick \"Force the use of a specific Steam Play compatibility tool\".")
	}
	return nil
}

// startGame runs the Burralis launcher directly; it starts WowClassic.exe and returns.
func startGame(_ map[string]string, game string) error {
	run := exec.Command(filepath.Join(game, launcherExe), "--version", "Classic", "--staticseed")
	run.Dir = game
	return run.Run()
}

// gameRunning reports whether a WowClassic.exe process exists (toolhelp process snapshot).
func gameRunning(string) bool {
	snap, err := syscall.CreateToolhelp32Snapshot(syscall.TH32CS_SNAPPROCESS, 0)
	if err != nil {
		return false
	}
	defer syscall.CloseHandle(snap)
	var pe syscall.ProcessEntry32
	pe.Size = uint32(unsafe.Sizeof(pe))
	for err = syscall.Process32First(snap, &pe); err == nil; err = syscall.Process32Next(snap, &pe) {
		if strings.EqualFold(syscall.UTF16ToString(pe.ExeFile[:]), "WowClassic.exe") {
			return true
		}
	}
	return false
}

func detach(cmd *exec.Cmd) {
	cmd.SysProcAttr = &syscall.SysProcAttr{HideWindow: true, CreationFlags: createNoWindow}
}

func stop(cmd *exec.Cmd) { cmd.Process.Kill() }

func errorDialog(msg string) {
	title, _ := syscall.UTF16PtrFromString("WotLK 3.4.3")
	text, _ := syscall.UTF16PtrFromString(msg)
	messageBox.Call(0, uintptr(unsafe.Pointer(text)), uintptr(unsafe.Pointer(title)), 0x10) // MB_ICONERROR
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

// startHotReload: hot-reload mode (hotreload = ... in wotlk343.ini) is only wired up on Linux.
func startHotReload(string, string) error {
	return fmt.Errorf("hotreload in wotlk343.ini is only supported on Linux")
}

func stopInstalledProxies(string) bool { return false }
