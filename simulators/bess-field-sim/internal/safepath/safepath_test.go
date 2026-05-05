package safepath_test

import (
	"os"
	"path/filepath"
	"testing"

	"github.com/pt9912/bess-ems/simulators/bess-field-sim/internal/safepath"
)

func TestCleanRelative_AllowsExistingPathWithinWorkingDirectory(t *testing.T) {
	root := chdirTempRoot(t)
	path := filepath.Join("fixtures", "scenario.json")
	if err := os.MkdirAll(filepath.Join(root, "fixtures"), 0o700); err != nil {
		t.Fatalf("mkdir: %v", err)
	}
	if err := os.WriteFile(filepath.Join(root, path), []byte("{}"), 0o600); err != nil {
		t.Fatalf("write fixture: %v", err)
	}

	got, err := safepath.CleanRelative(path)
	if err != nil {
		t.Fatalf("clean: %v", err)
	}
	if got != path {
		t.Fatalf("path: want %q, got %q", path, got)
	}
}

func TestCleanRelative_RejectsSymlinkEscapingWorkingDirectory(t *testing.T) {
	root := chdirTempRoot(t)
	outside := t.TempDir()
	if err := os.WriteFile(filepath.Join(outside, "secret.json"), []byte("{}"), 0o600); err != nil {
		t.Fatalf("write outside: %v", err)
	}
	if err := os.Symlink(outside, filepath.Join(root, "linked")); err != nil {
		t.Fatalf("symlink: %v", err)
	}

	_, err := safepath.CleanRelative(filepath.Join("linked", "secret.json"))
	if err == nil {
		t.Fatal("expected escaping symlink to be rejected")
	}
}

func TestCleanRelative_RejectsLexicalTraversal(t *testing.T) {
	chdirTempRoot(t)

	_, err := safepath.CleanRelative(filepath.Join("..", "scenario.json"))
	if err == nil {
		t.Fatal("expected traversal to be rejected")
	}
}

func TestCleanRelative_RejectsAbsolutePath(t *testing.T) {
	chdirTempRoot(t)

	_, err := safepath.CleanRelative(filepath.Join(string(filepath.Separator), "tmp", "scenario.json"))
	if err == nil {
		t.Fatal("expected absolute path to be rejected")
	}
}

func chdirTempRoot(t *testing.T) string {
	t.Helper()
	root := t.TempDir()
	wd, err := os.Getwd()
	if err != nil {
		t.Fatalf("getwd: %v", err)
	}
	if err := os.Chdir(root); err != nil {
		t.Fatalf("chdir temp root: %v", err)
	}
	t.Cleanup(func() {
		if err := os.Chdir(wd); err != nil {
			t.Fatalf("restore wd: %v", err)
		}
	})
	return root
}
