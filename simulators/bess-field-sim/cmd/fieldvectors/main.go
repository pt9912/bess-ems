// Command fieldvectors generates and gate-checks the field-authority MQTT
// golden vectors (ADR 0013 §5.2, docs/plan/planning/ in the repo root).
//
// The vectors are LIFTED from the real producer paths — ResolveTelemetry for
// telemetry/status/fault (including the fault-suppression contract) and the
// model.CommandAck marshal the CommandHandler publishes — never hand-listed:
// a hand-written list would reintroduce exactly the mirror drift ADR 0013
// exists to end. Comparison is structural (field-normative), not byte-based.
//
// The tool expects the REPO ROOT as working directory: it reads the shipped
// example mapping under config/examples/ and the committed manifest under
// config/schema/vectors/. It therefore runs from the root Dockerfile's
// field-vectors-* stages (`make field-vectors-check|-refresh`), NOT from the
// module-context `make simulator-*` builds, which cannot see those paths.
package main

import (
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"log/slog"
	"os"
	"path/filepath"
	"reflect"
	"time"

	"github.com/pt9912/bess-ems/simulators/bess-field-sim/internal/model"
	"github.com/pt9912/bess-ems/simulators/bess-field-sim/internal/mqtt"
)

const (
	// assetID is the fixed test asset substituted into {assetId} templates.
	assetID = "asset-1"
	// nominalCommandID ties the command_ack echo to the nominal command case
	// of the ems-authority manifest. The echo invariant is gate-checked by
	// the C# correlation harness (plan sub-slice 3), not just by convention.
	nominalCommandID = "cmd-golden-nominal"

	manifestVersion   = "golden-vector-manifest.v1"
	fieldManifestName = "mqtt-golden-vectors.field.v1.json"
	defaultMapping    = "config/examples/adapters/mqtt.simulator.json"
	defaultVectorsDir = "config/schema/vectors"
)

type vectorCase struct {
	Name        string          `json:"name"`
	TopicName   string          `json:"topic_name"`
	Direction   string          `json:"direction"`
	Topic       string          `json:"topic"`
	Retained    bool            `json:"retained"`
	Description string          `json:"description"`
	Payload     json.RawMessage `json:"payload,omitempty"`
	Suppressed  bool            `json:"suppressed,omitempty"`
}

type manifest struct {
	SchemaVersion string       `json:"schema_version"`
	Contract      string       `json:"contract"`
	Authority     string       `json:"authority"`
	Cases         []vectorCase `json:"cases"`
}

type topicInfo struct {
	topic    string
	retained bool
}

func main() {
	mode := flag.String("mode", "check", "check: compare regenerated vectors against the committed manifest; write: write the field manifest to -out")
	mapping := flag.String("mapping", defaultMapping, "mqtt mapping profile the vectors are lifted through")
	vectorsDir := flag.String("vectors-dir", defaultVectorsDir, "directory holding the committed vector manifests")
	out := flag.String("out", "", "output directory for -mode write (defaults to -vectors-dir)")
	flag.Parse()

	if err := run(*mode, *mapping, *vectorsDir, *out); err != nil {
		fmt.Fprintf(os.Stderr, "fieldvectors: %v\n", err)
		os.Exit(1)
	}
}

func run(mode, mappingPath, vectorsDir, out string) error {
	fresh, err := buildFieldManifest(mappingPath)
	if err != nil {
		return err
	}
	switch mode {
	case "write":
		if out == "" {
			out = vectorsDir
		}
		return writeManifest(fresh, filepath.Join(out, fieldManifestName))
	case "check":
		return checkFieldManifest(fresh, filepath.Join(vectorsDir, fieldManifestName))
	default:
		return fmt.Errorf("unknown -mode %q (want check or write)", mode)
	}
}

// buildFieldManifest lifts the field-authority cases through the real
// producer paths against the shipped example mapping.
func buildFieldManifest(mappingPath string) (manifest, error) {
	mapping, err := mqtt.LoadMapping(mappingPath)
	if err != nil {
		return manifest{}, fmt.Errorf("load mapping %q: %w", mappingPath, err)
	}
	topics := subscribeTopics(mapping)
	for _, name := range []string{"telemetry", "status", "fault", "command_ack"} {
		if _, ok := topics[name]; !ok {
			return manifest{}, fmt.Errorf("mapping %q declares no EMS-subscribe topic %q", mappingPath, name)
		}
	}

	nominal, err := resolveByName(nominalSnapshot(), mapping, topics)
	if err != nil {
		return manifest{}, err
	}
	charging, err := resolveByName(chargingSnapshot(), mapping, topics)
	if err != nil {
		return manifest{}, err
	}
	faulted, err := resolveByName(faultedSnapshot(), mapping, topics)
	if err != nil {
		return manifest{}, err
	}

	cases, err := assembleFieldCases(topics, nominal, charging, faulted)
	if err != nil {
		return manifest{}, err
	}
	return manifest{
		SchemaVersion: manifestVersion,
		Contract:      "mqtt",
		Authority:     "field",
		Cases:         cases,
	}, nil
}

// subscribeTopics indexes the EMS-`subscribe` topics by logical name with the
// {assetId} placeholder resolved to the fixed test asset.
func subscribeTopics(mapping model.MqttMapping) map[string]topicInfo {
	infos := make(map[string]topicInfo)
	for _, t := range mapping.Topics {
		if t.Direction != "subscribe" {
			continue
		}
		infos[t.Name] = topicInfo{
			topic:    mqtt.SubstituteAssetID(t.Topic, assetID),
			retained: t.Retained,
		}
	}
	return infos
}

// resolveByName runs the real producer path (ResolveTelemetry) and maps each
// emitted message back to its logical topic name via the resolved topic.
func resolveByName(snap model.TelemetrySnapshot, mapping model.MqttMapping, topics map[string]topicInfo) (map[string]mqtt.Resolved, error) {
	resolved, err := mqtt.ResolveTelemetry(snap, assetID, mapping)
	if err != nil {
		return nil, fmt.Errorf("resolve telemetry: %w", err)
	}
	names := make(map[string]string, len(topics))
	for name, info := range topics {
		names[info.topic] = name
	}
	out := make(map[string]mqtt.Resolved, len(resolved))
	for _, r := range resolved {
		name, ok := names[r.Topic]
		if !ok {
			return nil, fmt.Errorf("producer emitted a message on unmapped topic %q", r.Topic)
		}
		out[name] = r
	}
	return out, nil
}

// assembleFieldCases turns the resolved producer output into the fixed case
// set and enforces the emission invariants (fault suppression, presence).
func assembleFieldCases(topics map[string]topicInfo, nominal, charging, faulted map[string]mqtt.Resolved) ([]vectorCase, error) {
	if _, leaked := nominal["fault"]; leaked {
		return nil, errors.New(`producer emitted a fault message for fault_status "ok" — suppression contract broken`)
	}
	need := func(set map[string]mqtt.Resolved, name, input string) (mqtt.Resolved, error) {
		r, ok := set[name]
		if !ok {
			return mqtt.Resolved{}, fmt.Errorf("producer emitted no %q message for the %s snapshot", name, input)
		}
		return r, nil
	}
	telemetryNominal, err := need(nominal, "telemetry", "nominal")
	if err != nil {
		return nil, err
	}
	statusNominal, err := need(nominal, "status", "nominal")
	if err != nil {
		return nil, err
	}
	telemetryCharging, err := need(charging, "telemetry", "charging")
	if err != nil {
		return nil, err
	}
	faultActive, err := need(faulted, "fault", "faulted")
	if err != nil {
		return nil, err
	}
	ack, err := ackPayload()
	if err != nil {
		return nil, err
	}

	fault := topics["fault"]
	ackTopic := topics["command_ack"]
	return []vectorCase{
		fromResolved("telemetry-nominal", "telemetry", telemetryNominal,
			"Wide snapshot, one frame per tick and asset; nominal values from the ADR 0013 SUT smoke."),
		fromResolved("telemetry-charging", "telemetry", telemetryCharging,
			"Charging tick: negative active_power_kw with non-zero reactive power and DC current."),
		fromResolved("status-nominal", "status", statusNominal,
			"Focused subset {available, fault_status, offset_millis} the field republishes each tick."),
		fromResolved("fault-active", "fault", faultActive,
			`Emitted only while fault_status is outside {ok, ""}.`),
		{
			Name: "fault-suppressed-ok", TopicName: "fault", Direction: "subscribe",
			Topic: fault.topic, Retained: fault.retained,
			Description: `fault_status "ok" suppresses the fault message entirely: no payload may appear on the wire.`,
			Suppressed:  true,
		},
		{
			Name: "command-ack-accepted-echo", TopicName: "command_ack", Direction: "subscribe",
			Topic: ackTopic.topic, Retained: ackTopic.retained,
			Description: "Always-accepted echo the CommandHandler publishes; command_id echoes the nominal command case of the ems manifest.",
			Payload:     ack,
		},
	}, nil
}

func fromResolved(name, topicName string, r mqtt.Resolved, description string) vectorCase {
	return vectorCase{
		Name:        name,
		TopicName:   topicName,
		Direction:   "subscribe",
		Topic:       r.Topic,
		Retained:    r.Retained,
		Description: description,
		Payload:     json.RawMessage(r.Payload),
	}
}

// ackPayload marshals the same struct the CommandHandler publishes
// (internal/mqtt/commands.go), with a deterministic clock value.
func ackPayload() (json.RawMessage, error) {
	ack := model.CommandAck{
		CommandID:    nominalCommandID,
		Accepted:     true,
		DispatchedAt: time.Unix(0, 0).UTC(),
		Reason:       "accepted",
	}
	body, err := json.Marshal(ack)
	if err != nil {
		return nil, fmt.Errorf("marshal command ack: %w", err)
	}
	return body, nil
}

// Deterministic input snapshots. Values are fixed test data; the FIELD SET
// and encoding come from the producer code, which is what the vectors pin.
func nominalSnapshot() model.TelemetrySnapshot {
	return model.TelemetrySnapshot{
		OffsetMillis: 0, SocPercent: 60.5, SohPercent: 99,
		ActivePowerKw: 0, ReactivePowerKvar: 0,
		DcVoltage: 800, DcCurrent: 0, TemperatureCelsius: 22,
		Available: true, FaultStatus: "ok",
	}
}

func chargingSnapshot() model.TelemetrySnapshot {
	return model.TelemetrySnapshot{
		OffsetMillis: 1000, SocPercent: 61.2, SohPercent: 99,
		ActivePowerKw: -250.5, ReactivePowerKvar: 12.5,
		DcVoltage: 798.5, DcCurrent: -313.1, TemperatureCelsius: 23.5,
		Available: true, FaultStatus: "ok",
	}
}

func faultedSnapshot() model.TelemetrySnapshot {
	return model.TelemetrySnapshot{
		OffsetMillis: 2000, SocPercent: 60.9, SohPercent: 99,
		ActivePowerKw: 0, ReactivePowerKvar: 0,
		DcVoltage: 801, DcCurrent: 0, TemperatureCelsius: 58,
		Available: false, FaultStatus: "overtemperature",
	}
}

func writeManifest(m manifest, path string) error {
	body, err := json.MarshalIndent(m, "", "  ")
	if err != nil {
		return fmt.Errorf("encode manifest: %w", err)
	}
	body = append(body, '\n')
	if err := os.MkdirAll(filepath.Dir(path), 0o755); err != nil {
		return fmt.Errorf("create %q: %w", filepath.Dir(path), err)
	}
	if err := os.WriteFile(path, body, 0o644); err != nil {
		return fmt.Errorf("write %q: %w", path, err)
	}
	slog.Info("fieldvectors: manifest written", "path", path, "cases", len(m.Cases))
	return nil
}

// checkFieldManifest structurally compares the regenerated manifest against
// the committed one; byte differences (member order, whitespace) do not
// count, field differences do (ADR 0013 §3).
func checkFieldManifest(fresh manifest, path string) error {
	committed, err := os.ReadFile(path)
	if err != nil {
		return fmt.Errorf("read committed manifest (run 'make field-vectors-refresh' once after adding the producer cases): %w", err)
	}
	freshRaw, err := json.Marshal(fresh)
	if err != nil {
		return fmt.Errorf("encode regenerated manifest: %w", err)
	}
	equal, err := structurallyEqual(committed, freshRaw)
	if err != nil {
		return err
	}
	if !equal {
		reportDrift(committed, fresh)
		return errors.New("committed field vectors drifted from the producer code — run 'make field-vectors-refresh' and review the diff")
	}
	slog.Info("fieldvectors: committed manifest matches the producer (structural compare)", "path", path, "cases", len(fresh.Cases))
	return nil
}

func structurallyEqual(a, b []byte) (bool, error) {
	var av, bv any
	if err := json.Unmarshal(a, &av); err != nil {
		return false, fmt.Errorf("parse committed manifest: %w", err)
	}
	if err := json.Unmarshal(b, &bv); err != nil {
		return false, fmt.Errorf("parse regenerated manifest: %w", err)
	}
	return reflect.DeepEqual(av, bv), nil
}

// reportDrift prints a per-case diagnosis so the failure names the drifted
// case instead of dumping two whole manifests.
func reportDrift(committedRaw []byte, fresh manifest) {
	var committed manifest
	if err := json.Unmarshal(committedRaw, &committed); err != nil {
		fmt.Fprintf(os.Stderr, "fieldvectors: committed file does not parse as a manifest: %v\n", err)
		return
	}
	if committed.SchemaVersion != fresh.SchemaVersion || committed.Contract != fresh.Contract || committed.Authority != fresh.Authority {
		fmt.Fprintf(os.Stderr, "fieldvectors: manifest header drifted (committed %s/%s/%s, regenerated %s/%s/%s)\n",
			committed.SchemaVersion, committed.Contract, committed.Authority,
			fresh.SchemaVersion, fresh.Contract, fresh.Authority)
	}
	committedCases := caseIndex(committed.Cases)
	freshCases := caseIndex(fresh.Cases)
	for _, fc := range fresh.Cases {
		cc, ok := committedCases[fc.Name]
		if !ok {
			fmt.Fprintf(os.Stderr, "fieldvectors: case %q missing from the committed manifest\n", fc.Name)
			continue
		}
		if equal, err := structurallyEqual(caseJSON(cc), caseJSON(fc)); err == nil && !equal {
			fmt.Fprintf(os.Stderr, "fieldvectors: case %q drifted\n  committed:   %s\n  regenerated: %s\n",
				fc.Name, caseJSON(cc), caseJSON(fc))
		}
	}
	for _, cc := range committed.Cases {
		if _, ok := freshCases[cc.Name]; !ok {
			fmt.Fprintf(os.Stderr, "fieldvectors: case %q exists only in the committed manifest\n", cc.Name)
		}
	}
}

func caseIndex(cases []vectorCase) map[string]vectorCase {
	index := make(map[string]vectorCase, len(cases))
	for _, c := range cases {
		index[c.Name] = c
	}
	return index
}

func caseJSON(c vectorCase) []byte {
	body, err := json.Marshal(c)
	if err != nil {
		return []byte(fmt.Sprintf("%q", err.Error()))
	}
	return body
}
