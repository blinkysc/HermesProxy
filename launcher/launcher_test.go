package main

import (
	"encoding/json"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

func TestFindHermesPicksThisPlatform(t *testing.T) {
	root := t.TempDir()
	for _, d := range []string{"HermesProxy-MacOS-v1", "HermesProxy-Ubuntu-v1", "HermesProxy-Windows-v1"} {
		os.MkdirAll(filepath.Join(root, d), 0o755)
		os.WriteFile(filepath.Join(root, d, hermesBinary), nil, 0o755)
	}
	got := findHermes(root)
	if !strings.Contains(got, hermesDirHint) {
		t.Fatalf("picked %s, want the %s build", got, hermesDirHint)
	}
}

func TestConfigureHermesCert(t *testing.T) {
	p := filepath.Join(t.TempDir(), "appsettings.json")
	os.WriteFile(p, []byte(`{"ProxyNetworkOptions":{"CertificatePfxPath":"old.pfx","BNetPort":1119}}`), 0o644)
	read := func() map[string]any {
		var m map[string]any
		raw, _ := os.ReadFile(p)
		json.Unmarshal(raw, &m)
		return m["ProxyNetworkOptions"].(map[string]any)
	}
	if err := configureHermes(p, "10.0.0.1", "3724", ""); err != nil {
		t.Fatal(err)
	}
	if _, ok := read()["CertificatePfxPath"]; ok {
		t.Fatal("empty cert should remove CertificatePfxPath")
	}
	configureHermes(p, "10.0.0.1", "3724", "/x/localhost.pfx")
	if n := read(); n["CertificatePfxPath"] != "/x/localhost.pfx" || n["BNetPort"] != float64(1119) {
		t.Fatalf("got %v", n)
	}
}
