package scenario_test

import (
	"errors"
	"path/filepath"
	"testing"

	"github.com/pt9912/bess-ems/simulators/bess-field-sim/internal/model"
	"github.com/pt9912/bess-ems/simulators/bess-field-sim/internal/scenario"
	"github.com/pt9912/bess-ems/simulators/bess-field-sim/internal/testroot"
)

func TestMain(m *testing.M) {
	testroot.Main(m)
}

func TestLoadFromFile_HappyPath(t *testing.T) {
	t.Parallel()

	scn, err := scenario.LoadFromFile(repoFixture(t, "sim-m1-01-normal-discharge.json"))
	if err != nil {
		t.Fatalf("load: %v", err)
	}
	if scn.ID != "sim-m1-01" {
		t.Errorf("expected id sim-m1-01, got %q", scn.ID)
	}
	if scn.Asset.AssetID == "" {
		t.Error("expected asset id")
	}
	if len(scn.Telemetry) == 0 {
		t.Error("expected telemetry")
	}
}

func TestLoadFromFile_NotFound(t *testing.T) {
	t.Parallel()

	_, err := scenario.LoadFromFile("nonexistent.json")
	if err == nil {
		t.Fatal("expected error for missing file")
	}
}

func TestLoadFromFile_RejectsUnsafePath(t *testing.T) {
	t.Parallel()

	for _, path := range []string{"/nonexistent.json", "../scenario.json"} {
		_, err := scenario.LoadFromFile(path)
		if err == nil {
			t.Fatalf("expected error for unsafe path %q", path)
		}
	}
}

func TestLoadFromFile_MalformedJSON(t *testing.T) {
	t.Parallel()

	path := filepath.Join("testdata", "malformed", "scenario.invalid-json")
	_, err := scenario.LoadFromFile(path)
	if err == nil {
		t.Fatal("expected error for malformed JSON")
	}
}

func TestParse_RejectsMissingID(t *testing.T) {
	t.Parallel()

	data := []byte(`{"name":"x","asset":{"asset_id":"a"},"telemetry":[{"offset_millis":0,"available":true}]}`)
	_, err := scenario.Parse(data)
	if !errors.Is(err, scenario.ErrMissingID) {
		t.Fatalf("expected ErrMissingID, got %v", err)
	}
}

func TestParse_RejectsMissingName(t *testing.T) {
	t.Parallel()

	data := []byte(`{"id":"x","asset":{"asset_id":"a"},"telemetry":[{"offset_millis":0,"available":true}]}`)
	_, err := scenario.Parse(data)
	if !errors.Is(err, scenario.ErrMissingName) {
		t.Fatalf("expected ErrMissingName, got %v", err)
	}
}

func TestParse_RejectsMissingAssetID(t *testing.T) {
	t.Parallel()

	data := []byte(`{"id":"x","name":"x","asset":{},"telemetry":[{"offset_millis":0,"available":true}]}`)
	_, err := scenario.Parse(data)
	if !errors.Is(err, scenario.ErrMissingAssetID) {
		t.Fatalf("expected ErrMissingAssetID, got %v", err)
	}
}

func TestParse_RejectsEmptyTelemetry(t *testing.T) {
	t.Parallel()

	data := []byte(`{"id":"x","name":"x","asset":{"asset_id":"a"},"telemetry":[]}`)
	_, err := scenario.Parse(data)
	if !errors.Is(err, scenario.ErrEmptyTelemetry) {
		t.Fatalf("expected ErrEmptyTelemetry, got %v", err)
	}
}

func TestParse_RejectsNonMonotonicOffsets(t *testing.T) {
	t.Parallel()

	data := []byte(`{"id":"x","name":"x","asset":{"asset_id":"a"},"telemetry":[{"offset_millis":1000,"available":true},{"offset_millis":500,"available":true}]}`)
	_, err := scenario.Parse(data)
	if !errors.Is(err, scenario.ErrNonMonotonicOffsets) {
		t.Fatalf("expected ErrNonMonotonicOffsets, got %v", err)
	}
}

func TestValidate_RejectsEmptyAsset(t *testing.T) {
	t.Parallel()

	scn := model.Scenario{
		ID:        "x",
		Name:      "x",
		Telemetry: []model.TelemetrySnapshot{{OffsetMillis: 0, Available: true}},
	}
	if !errors.Is(scenario.Validate(scn), scenario.ErrMissingAssetID) {
		t.Fatal("expected ErrMissingAssetID")
	}
}

func repoFixture(t *testing.T, name string) string {
	t.Helper()
	return filepath.Join("testdata", "scenarios", name)
}
