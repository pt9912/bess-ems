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
            OperatingState.Init => trigger switch
            {
                StateTransitionTrigger.InitComplete => StateTransitionResult.Accept(OperatingState.Standby, "init-complete"),
                _ => Reject(current, trigger),
            },
            OperatingState.Standby => trigger switch
            {
                StateTransitionTrigger.Activate => StateTransitionResult.Accept(OperatingState.Ready, "activated"),
                StateTransitionTrigger.EnterMaintenance => StateTransitionResult.Accept(OperatingState.Maintenance, "maintenance-entered"),
                _ => Reject(current, trigger),
            },
            OperatingState.Ready => trigger switch
            {
                StateTransitionTrigger.GoIdle => StateTransitionResult.Accept(OperatingState.Idle, "idle-from-ready"),
                StateTransitionTrigger.BeginCharge => StateTransitionResult.Accept(OperatingState.Charging, "charge-begin"),
                StateTransitionTrigger.BeginDischarge => StateTransitionResult.Accept(OperatingState.Discharging, "discharge-begin"),
                StateTransitionTrigger.Deactivate => StateTransitionResult.Accept(OperatingState.Standby, "deactivated"),
                StateTransitionTrigger.EnterMaintenance => StateTransitionResult.Accept(OperatingState.Maintenance, "maintenance-entered"),
                _ => Reject(current, trigger),
            },
            OperatingState.Idle or OperatingState.Charging or OperatingState.Discharging => trigger switch
            {
                StateTransitionTrigger.GoIdle => StateTransitionResult.Accept(OperatingState.Idle, "idle"),
                StateTransitionTrigger.BeginCharge => StateTransitionResult.Accept(OperatingState.Charging, "charge-begin"),
                StateTransitionTrigger.BeginDischarge => StateTransitionResult.Accept(OperatingState.Discharging, "discharge-begin"),
                StateTransitionTrigger.HitLimit => StateTransitionResult.Accept(OperatingState.Limited, "limit-hit"),
                StateTransitionTrigger.Deactivate => StateTransitionResult.Accept(OperatingState.Ready, "deactivated"),
                _ => Reject(current, trigger),
            },
            OperatingState.Limited => trigger switch
            {
                StateTransitionTrigger.ExitLimit => StateTransitionResult.Accept(OperatingState.Ready, "limit-cleared"),
                StateTransitionTrigger.GoIdle => StateTransitionResult.Accept(OperatingState.Idle, "limit-cleared-to-idle"),
                StateTransitionTrigger.BeginCharge => StateTransitionResult.Accept(OperatingState.Charging, "limit-cleared-to-charging"),
                StateTransitionTrigger.BeginDischarge => StateTransitionResult.Accept(OperatingState.Discharging, "limit-cleared-to-discharging"),
                _ => Reject(current, trigger),
            },
            OperatingState.Fault => trigger switch
            {
                StateTransitionTrigger.Acknowledge => StateTransitionResult.Accept(OperatingState.Ready, "fault-acknowledged"),
                _ => Reject(current, trigger),
            },
            OperatingState.EmergencyStop => trigger switch
            {
                StateTransitionTrigger.Acknowledge => StateTransitionResult.Accept(OperatingState.Standby, "emergency-stop-acknowledged"),
                _ => Reject(current, trigger),
            },
            OperatingState.Maintenance => trigger switch
            {
                StateTransitionTrigger.ExitMaintenance => StateTransitionResult.Accept(OperatingState.Standby, "maintenance-exited"),
                _ => Reject(current, trigger),
            },
            _ => Reject(current, trigger),
        };
    }

    private static StateTransitionResult Reject(OperatingState current, StateTransitionTrigger trigger) =>
        StateTransitionResult.Reject(current, $"trigger {trigger} not allowed in state {current}");
}
