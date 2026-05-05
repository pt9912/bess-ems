// Package safepath normalizes operator-supplied file paths before the
// simulator reads local fixtures.
package safepath

import (
	"errors"
	"fmt"
	"path/filepath"
	"strings"
)

var (
	errAbsolutePath = errors.New("absolute paths are not allowed")
	errTraversal    = errors.New("path traversal is not allowed")
)

// CleanRelative returns a cleaned relative path or rejects inputs that can
// escape the process working directory.
func CleanRelative(path string) (string, error) {
	clean := filepath.Clean(path)
	if filepath.IsAbs(clean) {
		return "", fmt.Errorf("%w: %q", errAbsolutePath, path)
	}
	if clean == ".." || strings.HasPrefix(clean, ".."+string(filepath.Separator)) {
		return "", fmt.Errorf("%w: %q", errTraversal, path)
	}
	return clean, nil
}
