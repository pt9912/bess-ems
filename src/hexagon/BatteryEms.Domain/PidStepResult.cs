namespace BatteryEms.Domain;

public readonly record struct PidStepResult(
    PidControllerState NextState,
    double Output,
    bool WasClamped,
    bool WasIntegralFrozen);
