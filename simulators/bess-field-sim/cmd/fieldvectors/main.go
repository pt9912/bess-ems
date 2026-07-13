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
	"strings"
	"time"

	"github.com/pt9912/bess-ems/simulators/bess-field-sim/internal/modbus"
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

	manifestVersion         = "golden-vector-manifest.v1"
	fieldManifestName       = "mqtt-golden-vectors.field.v1.json"
	emsManifestName         = "mqtt-golden-vectors.ems.v1.json"
	modbusSimulatorManifest = "modbus-golden-vectors.simulator.v1.json"
	modbusHilManifest       = "modbus-golden-vectors.hil-simulator.v1.json"
	defaultMapping          = "config/examples/adapters/mqtt.simulator.json"
	defaultVectorsDir       = "config/schema/vectors"
	envelopeSchemaPath      = "config/schema/mqtt-telemetry-envelope.schema.json"
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
		if err := checkFieldManifest(fresh, filepath.Join(vectorsDir, fieldManifestName)); err != nil {
			return err
		}
		if err := checkEmsManifest(filepath.Join(vectorsDir, emsManifestName)); err != nil {
			return err
		}
		return checkModbusManifests(vectorsDir)
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

	// Every input pins the EXACT emitted message set: a new producer branch
	// (payloadFor emitting on a further topic) must fail here with "add a
	// golden case", never fall out of the manifest silently. The nominal and
	// charging inputs also pin the fault suppression (no fault message for
	// fault_status "ok").
	nominal, err := resolveByName(nominalSnapshot(), mapping, topics)
	if err == nil {
		err = assertEmittedExactly(nominal, []string{"telemetry", "status"}, "nominal")
	}
	if err != nil {
		return manifest{}, err
	}
	charging, err := resolveByName(chargingSnapshot(), mapping, topics)
	if err == nil {
		err = assertEmittedExactly(charging, []string{"telemetry", "status"}, "charging")
	}
	if err != nil {
		return manifest{}, err
	}
	faulted, err := resolveByName(faultedSnapshot(), mapping, topics)
	if err == nil {
		err = assertEmittedExactly(faulted, []string{"telemetry", "status", "fault"}, "faulted")
	}
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

// assertEmittedExactly fails on BOTH a missing and an extra producer message
// for the given input, so the golden cases always cover the full emitted
// surface (review finding 5).
func assertEmittedExactly(set map[string]mqtt.Resolved, want []string, input string) error {
	wanted := make(map[string]bool, len(want))
	for _, name := range want {
		wanted[name] = true
		if _, ok := set[name]; !ok {
			return fmt.Errorf("producer emitted no %q message for the %s snapshot", name, input)
		}
	}
	for name := range set {
		if !wanted[name] {
			return fmt.Errorf("producer emitted an unexpected %q message for the %s snapshot — add a golden case for it (or the suppression contract broke)", name, input)
		}
	}
	return nil
}

// assembleFieldCases turns the resolved producer output into the fixed case
// set (the emission invariants are already enforced by assertEmittedExactly).
func assembleFieldCases(topics map[string]topicInfo, nominal, charging, faulted map[string]mqtt.Resolved) ([]vectorCase, error) {
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

// ackPayload lifts the ack through the same policy constructor the
// CommandHandler publishes (mqtt.AcceptedEcho — review finding 6: the policy
// VALUES are shared code now, not a hand mirror), with a deterministic clock.
func ackPayload() (json.RawMessage, error) {
	ack := mqtt.AcceptedEcho(nominalCommandID, time.Unix(0, 0).UTC())
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

// checkEmsManifest decodes every command payload of the ems-authority
// manifest through the simulator's model.Command — proving the field side
// consumes what the EMS produces (plan sub-slice 4). Beyond decoding it pins
// (review finding 3): every payload key maps to a model.Command json tag
// (catches renamed AND added EMS fields after a vector refresh), the
// envelope's command `required` set is present, and the core values survive
// the round-trip (a rename like asset_id→assetId would otherwise decode to a
// silent zero value). Timestamps cover the C# `+00:00` RFC 3339 offset form;
// the WhenWritingNull-dropped reactive_power_kvar must land as a nil pointer.
// The negative direction (missing command_id → CommandHandler drops the
// message without an ACK) is pinned by internal/mqtt/commands_test.go.
func checkEmsManifest(path string) error {
	raw, err := os.ReadFile(path)
	if err != nil {
		return fmt.Errorf("read ems manifest: %w", err)
	}
	var m manifest
	if err := json.Unmarshal(raw, &m); err != nil {
		return fmt.Errorf("parse ems manifest: %w", err)
	}
	if m.Authority != "ems" {
		return fmt.Errorf("%s declares authority %q, want \"ems\"", path, m.Authority)
	}
	required, err := envelopeCommandRequired()
	if err != nil {
		return err
	}
	known := commandWireKeys()
	commands := 0
	for _, c := range m.Cases {
		if c.TopicName != "command" || c.Payload == nil {
			continue
		}
		if err := checkCommandCase(c, known, required); err != nil {
			return err
		}
		commands++
	}
	if commands == 0 {
		return fmt.Errorf("%s carries no command case — nothing to consume", path)
	}
	slog.Info("fieldvectors: ems manifest decodes through model.Command", "path", path, "commands", commands)
	return nil
}

func checkCommandCase(c vectorCase, known map[string]bool, required []string) error {
	var cmd model.Command
	if err := json.Unmarshal(c.Payload, &cmd); err != nil {
		return fmt.Errorf("case %q: payload does not decode into model.Command: %w", c.Name, err)
	}
	var probe map[string]json.RawMessage
	if err := json.Unmarshal(c.Payload, &probe); err != nil {
		return fmt.Errorf("case %q: payload does not probe as an object: %w", c.Name, err)
	}
	if err := checkCommandKeys(c.Name, probe, known, required); err != nil {
		return err
	}
	return checkCommandValues(c.Name, cmd, probe)
}

func checkCommandKeys(name string, probe map[string]json.RawMessage, known map[string]bool, required []string) error {
	for key := range probe {
		if !known[key] {
			return fmt.Errorf("case %q: payload key %q has no model.Command field — the field consumer would drop it silently", name, key)
		}
	}
	for _, key := range required {
		if _, ok := probe[key]; !ok {
			return fmt.Errorf("case %q: envelope-required command key %q is missing from the payload", name, key)
		}
	}
	return nil
}

func checkCommandValues(name string, cmd model.Command, probe map[string]json.RawMessage) error {
	if cmd.CommandID == "" {
		return fmt.Errorf("case %q: command_id is empty — the CommandHandler would drop it without an ACK", name)
	}
	if cmd.AssetID != assetID {
		return fmt.Errorf("case %q: asset_id decoded to %q, want %q — a renamed wire field decodes to a silent zero value", name, cmd.AssetID, assetID)
	}
	if cmd.ActivePowerKw == 0 {
		return fmt.Errorf("case %q: active_power_kw decoded to 0 — golden commands carry non-zero power so a dropped field is visible", name)
	}
	if cmd.Reason == "" {
		return fmt.Errorf("case %q: reason decoded to empty", name)
	}
	if !vocab(model.WireModes())[cmd.Mode] {
		return fmt.Errorf("case %q: mode %q is outside the wire vocabulary %v", name, cmd.Mode, model.WireModes())
	}
	if !vocab(model.WireSources())[cmd.Source] {
		return fmt.Errorf("case %q: source %q is outside the wire vocabulary %v", name, cmd.Source, model.WireSources())
	}
	if cmd.Timestamp.IsZero() || cmd.ValidUntil.IsZero() {
		return fmt.Errorf("case %q: timestamp/valid_until did not decode as RFC 3339", name)
	}
	_, present := probe["reactive_power_kvar"]
	if present != (cmd.ReactivePowerKvar != nil) {
		return fmt.Errorf("case %q: reactive_power_kvar presence (%t) does not round into the pointer field (nil=%t)", name, present, cmd.ReactivePowerKvar == nil)
	}
	return nil
}

func vocab(entries []string) map[string]bool {
	set := make(map[string]bool, len(entries))
	for _, entry := range entries {
		set[entry] = true
	}
	return set
}

// commandWireKeys reflects the json tag names off model.Command — the
// authoritative Go-side key set, never hand-listed.
func commandWireKeys() map[string]bool {
	commandType := reflect.TypeOf(model.Command{})
	keys := make(map[string]bool, commandType.NumField())
	for i := range commandType.NumField() {
		tag := commandType.Field(i).Tag.Get("json")
		name, _, _ := strings.Cut(tag, ",")
		if name != "" && name != "-" {
			keys[name] = true
		}
	}
	return keys
}

// envelopeCommandRequired reads the published envelope schema's command
// `required` set — lifted from the contract artefact, not re-listed here.
func envelopeCommandRequired() ([]string, error) {
	raw, err := os.ReadFile(envelopeSchemaPath)
	if err != nil {
		return nil, fmt.Errorf("read envelope schema: %w", err)
	}
	var envelope struct {
		Defs map[string]struct {
			Required []string `json:"required"`
		} `json:"$defs"`
	}
	if err := json.Unmarshal(raw, &envelope); err != nil {
		return nil, fmt.Errorf("parse envelope schema: %w", err)
	}
	command, ok := envelope.Defs["command"]
	if !ok || len(command.Required) == 0 {
		return nil, fmt.Errorf("%s carries no $defs.command.required set", envelopeSchemaPath)
	}
	return command.Required, nil
}

// --- Modbus conformance (ADR 0013 §5.4, plan sub-slice 3b) -----------------

type modbusCase struct {
	Name          string   `json:"name"`
	Register      string   `json:"register"`
	Direction     string   `json:"direction"`
	RegisterTable string   `json:"register_table"`
	Address       int      `json:"address"`
	Type          string   `json:"type"`
	WordOrder     string   `json:"word_order"`
	ScaleFactor   float64  `json:"scale_factor"`
	Value         float64  `json:"value"`
	Words         []uint16 `json:"words"`
}

type modbusManifest struct {
	SchemaVersion string       `json:"schema_version"`
	Contract      string       `json:"contract"`
	Authority     string       `json:"authority"`
	Profile       string       `json:"profile"`
	Cases         []modbusCase `json:"cases"`
}

// checkModbusManifests gates the simulator against the C#-lifted Modbus
// vectors: for every read case whose register name the simulator serves
// (modbus.TelemetryRegisterNames — grid_* registers deliberately have no
// simulator value and stay contract for external producers), the REAL
// encoder path must produce exactly the vector words on the case's table.
// The input snapshot is constructed FROM the committed manifests' value
// fields (plan-review finding 6: a re-listed Go value table would be the
// very mirror drift ADR 0013 ends), including the reverse mapping of the
// two non-numeric snapshot fields (available 1<->true, fault_status
// 0<->"ok"). The case<->profile equality itself is pinned by the python
// gate, so a single-register mapping per case runs the real path without
// the simulator having to load profiles with foreign register names.
func checkModbusManifests(vectorsDir string) error {
	simNames := make(map[string]bool)
	for _, name := range modbus.TelemetryRegisterNames() {
		simNames[name] = true
	}
	for _, name := range []string{modbusSimulatorManifest, modbusHilManifest} {
		if err := checkModbusManifest(filepath.Join(vectorsDir, name), simNames); err != nil {
			return err
		}
	}
	return nil
}

func checkModbusManifest(path string, simNames map[string]bool) error {
	raw, err := os.ReadFile(path)
	if err != nil {
		return fmt.Errorf("read modbus manifest: %w", err)
	}
	var m modbusManifest
	if err := json.Unmarshal(raw, &m); err != nil {
		return fmt.Errorf("parse modbus manifest: %w", err)
	}
	if m.Contract != "modbus" || m.Authority != "ems" {
		return fmt.Errorf("%s declares contract=%q authority=%q, want modbus/ems", path, m.Contract, m.Authority)
	}
	snap, err := snapshotFromCases(path, m.Cases, simNames)
	if err != nil {
		return err
	}
	checked := 0
	for _, c := range m.Cases {
		if c.Direction != "read" || !simNames[c.Register] {
			continue
		}
		if err := checkModbusCase(path, c, snap); err != nil {
			return err
		}
		checked++
	}
	if checked == 0 {
		return fmt.Errorf("%s carries no simulator-served read case — conformance gate would be vacuous", path)
	}
	slog.Info("fieldvectors: simulator encoder matches the modbus vectors", "path", path, "cases", checked)
	return nil
}

// snapshotFromCases builds the encoder input from the manifest's own value
// fields; duplicate register names with conflicting values are an error.
func snapshotFromCases(path string, cases []modbusCase, simNames map[string]bool) (model.TelemetrySnapshot, error) {
	var snap model.TelemetrySnapshot
	seen := make(map[string]float64)
	for _, c := range cases {
		if c.Direction != "read" || !simNames[c.Register] {
			continue
		}
		if prev, dup := seen[c.Register]; dup && prev != c.Value {
			return snap, fmt.Errorf("%s: register %q carries conflicting values (%g vs %g)", path, c.Register, prev, c.Value)
		}
		seen[c.Register] = c.Value
		applySnapshotValue(&snap, c.Register, c.Value)
	}
	return snap, nil
}

// applySnapshotValue is the reverse of the encoder's valueFor mapping.
func applySnapshotValue(snap *model.TelemetrySnapshot, name string, value float64) {
	switch name {
	case "soc_percent":
		snap.SocPercent = value
	case "soh_percent":
		snap.SohPercent = value
	case "active_power_kw":
		snap.ActivePowerKw = value
	case "reactive_power_kvar":
		snap.ReactivePowerKvar = value
	case "dc_voltage":
		snap.DcVoltage = value
	case "dc_current":
		snap.DcCurrent = value
	case "temperature_celsius":
		snap.TemperatureCelsius = value
	case "available":
		snap.Available = value >= 1
	case "fault_status":
		if value == 0 {
			snap.FaultStatus = "ok"
		} else {
			snap.FaultStatus = "fault-active"
		}
	}
}

func checkModbusCase(path string, c modbusCase, snap model.TelemetrySnapshot) error {
	mapping := model.ModbusMapping{
		ProfileName:     "conformance",
		UnitIDDiscovery: "none",
		Registers: []model.ModbusRegister{{
			Name:          c.Register,
			Address:       c.Address,
			Type:          c.Type,
			RegisterTable: c.RegisterTable,
			WordOrder:     c.WordOrder,
			ScaleFactor:   c.ScaleFactor,
		}},
	}
	// The single-register mapping runs through the same structural
	// validation the simulator applies to real fixtures (review finding 8b:
	// notably the address+wordcount bound the server's applyWords would
	// otherwise enforce only by silently dropping the second word).
	if err := modbus.ValidateMapping(mapping); err != nil {
		return fmt.Errorf("%s: case %q does not form a valid simulator mapping: %w", path, c.Name, err)
	}
	image := modbus.EncodeSnapshot(snap, mapping)
	space := image.Holding
	if c.RegisterTable == modbus.TableInput {
		space = image.Input
	}
	for i, want := range c.Words {
		got, ok := space[c.Address+i]
		if !ok {
			return fmt.Errorf("%s: case %q word %d at address %d: simulator encoder produced no word there, vector pins %d — run the encoder against ADR 0013 §5.4 (register_table/word_order drift?)",
				path, c.Name, i, c.Address+i, want)
		}
		if got != want {
			return fmt.Errorf("%s: case %q word %d at address %d: simulator encoder produced %d, vector pins %d — run the encoder against ADR 0013 §5.4 (register_table/word_order drift?)",
				path, c.Name, i, c.Address+i, got, want)
		}
	}
	return nil
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
