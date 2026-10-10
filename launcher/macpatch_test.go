package main

import (
	"bytes"
	"os"
	"path/filepath"
	"testing"
)

// Patches a copy of the real Mac client when it is present next to the launcher source.
func TestPatchMacClient(t *testing.T) {
	src := filepath.Join("..", gameDir, "World of Warcraft Classic.app", "Contents", "MacOS", "World of Warcraft Classic")
	orig, err := os.ReadFile(src)
	if err != nil {
		t.Skip("Mac client not present")
	}
	exe := filepath.Join(t.TempDir(), "client")
	os.WriteFile(exe, orig, 0o755)
	changed, err := patchMacClient(exe)
	if err != nil || !changed {
		t.Fatalf("first patch: changed=%v err=%v", changed, err)
	}
	got, _ := os.ReadFile(exe)
	if len(got) != len(orig) || bytes.Contains(got, []byte(".actual.battle.net")) {
		t.Fatal("size changed or portal still present")
	}
	if c := bytes.Count(got, tcRSAModulus); c != 2 {
		t.Fatalf("TC modulus count %d, want 2 (one per slice)", c)
	}
	if c := bytes.Count(got, tcEd25519PublicKey); c != 2 {
		t.Fatalf("TC Ed25519 count %d, want 2", c)
	}
	diff := 0
	for i := range got {
		if got[i] != orig[i] {
			diff++
		}
	}
	t.Logf("bytes changed: %d", diff)
	if changed, err = patchMacClient(exe); changed || err != nil {
		t.Fatalf("second patch should be a no-op: changed=%v err=%v", changed, err)
	}
}
