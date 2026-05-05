namespace BatteryEms.Domain;

public enum StateTransitionTrigger
{
    InitComplete,
    Activate,
    Deactivate,
    BeginCharge,
    BeginDischarge,
    GoIdle,
    HitLimit,
    ExitLimit,
    FaultDetected,
    Acknowledge,
    EmergencyStop,
    EnterMaintenance,
    ExitMaintenance,
}

public sealed record StateTransitionResult(OperatingState NewState, bool Accepted, string Reason)
{
    public static StateTransitionResult Accept(OperatingState newState, string reason) =>
        new(newState, true, reason);

    public static StateTransitionResult Reject(OperatingState currentState, string reason) =>
        new(currentState, false, reason);
}

public static class StateMachine
{
    public static StateTransitionResult Apply(OperatingState current, StateTransitionTrigger trigger)
    {
        if (trigger == StateTransitionTrigger.EmergencyStop)
        {
            return StateTransitionResult.Accept(OperatingState.EmergencyStop, "emergency-stop");
        }

        if (trigger == StateTransitionTrigger.FaultDetected && current != OperatingState.EmergencyStop)
        {
            return StateTransitionResult.Accept(OperatingState.Fault, "fault-detected");
        }

        return current switch
        {
            OperatingState.Init => HandleInit(trigger, current),
            OperatingState.Standby => HandleStandby(trigger, current),
            OperatingState.Ready => HandleReady(trigger, current),
            OperatingState.Idle or OperatingState.Charging or OperatingState.Discharging
                => HandleActive(trigger, current),
            OperatingState.Limited => HandleLimited(trigger, current),
            OperatingState.Fault => HandleFault(trigger, current),
            OperatingState.EmergencyStop => HandleEmergencyStop(trigger, current),
            OperatingState.Maintenance => HandleMaintenance(trigger, current),
            _ => Reject(current, trigger),
        };
    }

    private static StateTransitionResult HandleInit(StateTransitionTrigger trigger, OperatingState current) =>
        trigger switch
        {
            StateTransitionTrigger.InitComplete => StateTransitionResult.Accept(OperatingState.Standby, "init-complete"),
            _ => Reject(current, trigger),
        };

    private static StateTransitionResult HandleStandby(StateTransitionTrigger trigger, OperatingState current) =>
        trigger switch
        {
            StateTransitionTrigger.Activate => StateTransitionResult.Accept(OperatingState.Ready, "activated"),
            StateTransitionTrigger.EnterMaintenance => StateTransitionResult.Accept(OperatingState.Maintenance, "maintenance-entered"),
            _ => Reject(current, trigger),
        };

    private static StateTransitionResult HandleReady(StateTransitionTrigger trigger, OperatingState current) =>
        trigger switch
        {
            StateTransitionTrigger.GoIdle => StateTransitionResult.Accept(OperatingState.Idle, "idle-from-ready"),
            StateTransitionTrigger.BeginCharge => StateTransitionResult.Accept(OperatingState.Charging, "charge-begin"),
            StateTransitionTrigger.BeginDischarge => StateTransitionResult.Accept(OperatingState.Discharging, "discharge-begin"),
            StateTransitionTrigger.Deactivate => StateTransitionResult.Accept(OperatingState.Standby, "deactivated"),
            StateTransitionTrigger.EnterMaintenance => StateTransitionResult.Accept(OperatingState.Maintenance, "maintenance-entered"),
            _ => Reject(current, trigger),
        };

    private static StateTransitionResult HandleActive(StateTransitionTrigger trigger, OperatingState current) =>
        trigger switch
        {
            StateTransitionTrigger.GoIdle => StateTransitionResult.Accept(OperatingState.Idle, "idle"),
            StateTransitionTrigger.BeginCharge => StateTransitionResult.Accept(OperatingState.Charging, "charge-begin"),
            StateTransitionTrigger.BeginDischarge => StateTransitionResult.Accept(OperatingState.Discharging, "discharge-begin"),
            StateTransitionTrigger.HitLimit => StateTransitionResult.Accept(OperatingState.Limited, "limit-hit"),
            StateTransitionTrigger.Deactivate => StateTransitionResult.Accept(OperatingState.Ready, "deactivated"),
            _ => Reject(current, trigger),
        };

    private static StateTransitionResult HandleLimited(StateTransitionTrigger trigger, OperatingState current) =>
        trigger switch
        {
            StateTransitionTrigger.ExitLimit => StateTransitionResult.Accept(OperatingState.Ready, "limit-cleared"),
            StateTransitionTrigger.GoIdle => StateTransitionResult.Accept(OperatingState.Idle, "limit-cleared-to-idle"),
            StateTransitionTrigger.BeginCharge => StateTransitionResult.Accept(OperatingState.Charging, "limit-cleared-to-charging"),
            StateTransitionTrigger.BeginDischarge => StateTransitionResult.Accept(OperatingState.Discharging, "limit-cleared-to-discharging"),
            _ => Reject(current, trigger),
        };

    private static StateTransitionResult HandleFault(StateTransitionTrigger trigger, OperatingState current) =>
        trigger switch
        {
            StateTransitionTrigger.Acknowledge => StateTransitionResult.Accept(OperatingState.Ready, "fault-acknowledged"),
            _ => Reject(current, trigger),
        };

    private static StateTransitionResult HandleEmergencyStop(StateTransitionTrigger trigger, OperatingState current) =>
        trigger switch
        {
            StateTransitionTrigger.Acknowledge => StateTransitionResult.Accept(OperatingState.Standby, "emergency-stop-acknowledged"),
            _ => Reject(current, trigger),
        };

    private static StateTransitionResult HandleMaintenance(StateTransitionTrigger trigger, OperatingState current) =>
        trigger switch
        {
            StateTransitionTrigger.ExitMaintenance => StateTransitionResult.Accept(OperatingState.Standby, "maintenance-exited"),
            _ => Reject(current, trigger),
        };

    private static StateTransitionResult Reject(OperatingState current, StateTransitionTrigger trigger) =>
        StateTransitionResult.Reject(current, $"trigger {trigger} not allowed in state {current}");
}
