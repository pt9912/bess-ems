// Package testroot contains helpers for tests that need the simulator root as
// their working directory.
package testroot

import (
	"os"
	"path/filepath"
	"runtime"
)

type runner interface {
	Run() int
}

// Main changes to the simulator root and exits with m.Run's status.
func Main(m runner) {
	_, file, _, ok := runtime.Caller(0)
	if !ok {
		os.Exit(1)
	}
	root, err := findModuleRoot(filepath.Dir(file))
	if err != nil {
		os.Exit(1)
	}
	if err := os.Chdir(root); err != nil {
		os.Exit(1)
	}
	os.Exit(m.Run())
}

func findModuleRoot(start string) (string, error) {
	dir := start
	for {
		if _, err := os.Stat(filepath.Join(dir, "go.mod")); err == nil {
			return dir, nil
		} else if !os.IsNotExist(err) {
			return "", err
		}

		parent := filepath.Dir(dir)
		if parent == dir {
			return "", os.ErrNotExist
		}
		dir = parent
	}
}
