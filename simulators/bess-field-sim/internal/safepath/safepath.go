// Package safepath normalizes operator-supplied file paths before the
// simulator reads local fixtures.
package safepath

import (
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"strings"
)

var (
	errAbsolutePath = errors.New("absolute paths are not allowed")
	errTraversal    = errors.New("path traversal is not allowed")
)

// CleanRelative returns a cleaned relative path or rejects inputs that can
// escape the process working directory. It resolves symlinks against os.Getwd,
// so callers must not change the working directory concurrently.
func CleanRelative(path string) (string, error) {
	clean := filepath.Clean(path)
	if filepath.IsAbs(clean) {
		return "", fmt.Errorf("%w: %q", errAbsolutePath, path)
	}
	if clean == ".." || strings.HasPrefix(clean, ".."+string(filepath.Separator)) {
		return "", fmt.Errorf("%w: %q", errTraversal, path)
	}
	if err := ensureWithinWorkingDirectory(clean); err != nil {
		return "", err
	}
	return clean, nil
}

func ensureWithinWorkingDirectory(path string) error {
	root, err := os.Getwd()
	if err != nil {
		return fmt.Errorf("resolve working directory: %w", err)
	}
	realRoot, err := filepath.EvalSymlinks(root)
	if err != nil {
		if errors.Is(err, os.ErrNotExist) {
			realRoot = root
		} else {
			return fmt.Errorf("resolve working directory symlinks: %w", err)
		}
	}

	absPath, err := filepath.Abs(path)
	if err != nil {
		return fmt.Errorf("resolve path %q: %w", path, err)
	}
	realPath, err := filepath.EvalSymlinks(absPath)
	if err != nil {
		if errors.Is(err, os.ErrNotExist) {
			return nil
		}
		return fmt.Errorf("resolve path symlinks %q: %w", path, err)
	}

	rel, err := filepath.Rel(realRoot, realPath)
	if err != nil {
		return fmt.Errorf("compare path %q to working directory: %w", path, err)
	}
	if rel == ".." || strings.HasPrefix(rel, ".."+string(filepath.Separator)) {
		return fmt.Errorf("%w: %q", errTraversal, path)
	}
	return nil
}
