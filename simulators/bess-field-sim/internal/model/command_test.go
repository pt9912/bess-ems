package model_test

import (
	"testing"

	"github.com/pt9912/bess-ems/simulators/bess-field-sim/internal/model"
)

// Pins the wire vocabularies to the .NET enum names (BatteryCommand.cs /
// MqttPayloads.cs). A rename on either side must surface here or in the
// golden-vector consumption check — never drift silently.
func TestWireVocabulariesMatchTheDotnetEnumNames(t *testing.T) {
	t.Parallel()

	wantModes := []string{"Stop", "Charge", "Discharge", "Idle"}
	gotModes := model.WireModes()
	if len(gotModes) != len(wantModes) {
		t.Fatalf("WireModes() = %v, want %v", gotModes, wantModes)
	}
	for i, want := range wantModes {
		if gotModes[i] != want {
			t.Fatalf("WireModes()[%d] = %q, want %q", i, gotModes[i], want)
		}
	}

	wantSources := []string{"Schedule", "Operator", "RegelLeistung", "Safety", "Optimization", "Fallback"}
	gotSources := model.WireSources()
	if len(gotSources) != len(wantSources) {
		t.Fatalf("WireSources() = %v, want %v", gotSources, wantSources)
	}
	for i, want := range wantSources {
		if gotSources[i] != want {
			t.Fatalf("WireSources()[%d] = %q, want %q", i, gotSources[i], want)
		}
	}
}
