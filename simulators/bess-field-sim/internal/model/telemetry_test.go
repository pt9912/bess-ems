package model_test

import (
	"testing"
	"time"

	"github.com/pt9912/bess-ems/simulators/bess-field-sim/internal/model"
)

func TestTelemetrySnapshot_AbsoluteTime(t *testing.T) {
	t.Parallel()

	start := time.Date(2026, 5, 5, 12, 0, 0, 0, time.UTC)
	snap := model.TelemetrySnapshot{OffsetMillis: 1500}

	got := snap.AbsoluteTime(start)
	want := start.Add(1500 * time.Millisecond)
	if !got.Equal(want) {
		t.Errorf("AbsoluteTime = %v, want %v", got, want)
	}
}

func TestTelemetrySnapshot_AbsoluteTime_ZeroOffset(t *testing.T) {
	t.Parallel()

	start := time.Date(2026, 5, 5, 12, 0, 0, 0, time.UTC)
	snap := model.TelemetrySnapshot{OffsetMillis: 0}

	got := snap.AbsoluteTime(start)
	if !got.Equal(start) {
		t.Errorf("AbsoluteTime = %v, want %v", got, start)
	}
}
