package scenario_test

import (
	"path/filepath"
	"runtime"
	"sort"
	"testing"

	"github.com/pt9912/bess-ems/simulators/bess-field-sim/internal/scenario"
)

// TestAllFixturesLoad walks testdata/scenarios/*.json and asserts that
// every shipped fixture parses and validates. Adds a regression net for
// new SIM-M1-XX fixtures: dropping a malformed file in the directory
// will fail the gate.
func TestAllFixturesLoad(t *testing.T) {
	t.Parallel()

	dir := fixtureDir(t)
	matches, err := filepath.Glob(filepath.Join(dir, "*.json"))
	if err != nil {
		t.Fatalf("glob: %v", err)
	}
	if len(matches) == 0 {
		t.Fatalf("no fixtures in %s", dir)
	}
	sort.Strings(matches)

	for _, path := range matches {
		t.Run(filepath.Base(path), func(t *testing.T) {
			t.Parallel()

			scn, err := scenario.LoadFromFile(path)
			if err != nil {
				t.Fatalf("load %s: %v", path, err)
			}
			if scn.ID == "" {
				t.Errorf("%s: id missing", filepath.Base(path))
			}
			if len(scn.Telemetry) == 0 {
				t.Errorf("%s: telemetry empty", filepath.Base(path))
			}
		})
	}
}

func fixtureDir(t *testing.T) string {
	t.Helper()
	_, file, _, ok := runtime.Caller(0)
	if !ok {
		t.Fatal("could not resolve caller")
	}
	root := filepath.Join(filepath.Dir(file), "..", "..")
	return filepath.Join(root, "testdata", "scenarios")
}
