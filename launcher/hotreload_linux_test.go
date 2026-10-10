//go:build linux

package main

import (
	"os"
	"os/exec"
	"path/filepath"
	"testing"
)

// stopInstalledProxies stops processes running exactly that binary and leaves others alone.
func TestStopInstalledProxies(t *testing.T) {
	sleep, err := exec.LookPath("sleep")
	if err != nil {
		t.Skip("no sleep binary")
	}
	data, _ := os.ReadFile(sleep)
	dir := t.TempDir()
	target, other := filepath.Join(dir, "HermesProxy"), filepath.Join(dir, "Other")
	os.WriteFile(target, data, 0o755)
	os.WriteFile(other, data, 0o755)

	victim := exec.Command(target, "30")
	bystander := exec.Command(other, "30")
	if err := victim.Start(); err != nil {
		t.Fatal(err)
	}
	if err := bystander.Start(); err != nil {
		t.Fatal(err)
	}
	defer bystander.Process.Kill()
	victimDone := make(chan struct{})
	go func() { victim.Wait(); close(victimDone) }()

	if !stopInstalledProxies(target) {
		t.Fatal("reported no proxy stopped")
	}
	select {
	case <-victimDone:
	default:
		t.Fatal("process running the installed binary still alive")
	}
	if bystander.ProcessState != nil {
		t.Fatal("unrelated process was stopped")
	}
	if stopInstalledProxies(target) {
		t.Fatal("reported a proxy when none was running")
	}
}

func TestExpandHome(t *testing.T) {
	t.Setenv("HOME", "/home/x")
	if got := expandHome("~/Downloads/HermesProxy"); got != "/home/x/Downloads/HermesProxy" {
		t.Fatalf("got %q", got)
	}
	if got := expandHome("/abs/path"); got != "/abs/path" {
		t.Fatalf("got %q", got)
	}
}
