using BatteryEms.Domain;
using Xunit;

namespace BatteryEms.Domain.Tests;

public sealed class StateMachineTests
{
    [Fact]
    public void Init_completes_to_standby()
    {
        var result = StateMachine.Apply(OperatingState.Init, StateTransitionTrigger.InitComplete);
        Assert.True(result.Accepted);
        Assert.Equal(OperatingState.Standby, result.NewState);
    }

    [Fact]
    public void Standby_activates_to_ready()
    {
        var result = StateMachine.Apply(OperatingState.Standby, StateTransitionTrigger.Activate);
        Assert.True(result.Accepted);
        Assert.Equal(OperatingState.Ready, result.NewState);
    }

    [Theory]
    [InlineData(OperatingState.Ready, OperatingState.Idle, StateTransitionTrigger.GoIdle)]
    [InlineData(OperatingState.Ready, OperatingState.Charging, StateTransitionTrigger.BeginCharge)]
    [InlineData(OperatingState.Ready, OperatingState.Discharging, StateTransitionTrigger.BeginDischarge)]
    [InlineData(OperatingState.Idle, OperatingState.Charging, StateTransitionTrigger.BeginCharge)]
    [InlineData(OperatingState.Charging, OperatingState.Discharging, StateTransitionTrigger.BeginDischarge)]
    [InlineData(OperatingState.Discharging, OperatingState.Charging, StateTransitionTrigger.BeginCharge)]
    public void Active_states_compose_freely(OperatingState from, OperatingState to, StateTransitionTrigger trigger)
    {
        var result = StateMachine.Apply(from, trigger);
        Assert.True(result.Accepted);
        Assert.Equal(to, result.NewState);
    }

    [Theory]
    [InlineData(OperatingState.Idle)]
    [InlineData(OperatingState.Charging)]
    [InlineData(OperatingState.Discharging)]
    public void Active_states_can_hit_limit(OperatingState from)
    {
        var result = StateMachine.Apply(from, StateTransitionTrigger.HitLimit);
        Assert.True(result.Accepted);
        Assert.Equal(OperatingState.Limited, result.NewState);
    }

    [Theory]
    [InlineData(StateTransitionTrigger.ExitLimit, OperatingState.Ready)]
    [InlineData(StateTransitionTrigger.GoIdle, OperatingState.Idle)]
    [InlineData(StateTransitionTrigger.BeginCharge, OperatingState.Charging)]
    [InlineData(StateTransitionTrigger.BeginDischarge, OperatingState.Discharging)]
    public void Limited_can_exit_to_active_states(StateTransitionTrigger trigger, OperatingState expected)
    {
        var result = StateMachine.Apply(OperatingState.Limited, trigger);
        Assert.True(result.Accepted);
        Assert.Equal(expected, result.NewState);
    }

    [Theory]
    [Trait("Category", "Safety")]
    [InlineData(OperatingState.Init)]
    [InlineData(OperatingState.Standby)]
    [InlineData(OperatingState.Ready)]
    [InlineData(OperatingState.Idle)]
    [InlineData(OperatingState.Charging)]
    [InlineData(OperatingState.Discharging)]
    [InlineData(OperatingState.Limited)]
    [InlineData(OperatingState.Fault)]
    [InlineData(OperatingState.Maintenance)]
    public void Emergency_stop_is_reachable_from_every_state_except_emergency_stop(OperatingState from)
    {
        var result = StateMachine.Apply(from, StateTransitionTrigger.EmergencyStop);
        Assert.True(result.Accepted);
        Assert.Equal(OperatingState.EmergencyStop, result.NewState);
    }

    [Theory]
    [Trait("Category", "Safety")]
    [InlineData(OperatingState.Standby)]
    [InlineData(OperatingState.Ready)]
    [InlineData(OperatingState.Idle)]
    [InlineData(OperatingState.Charging)]
    [InlineData(OperatingState.Discharging)]
    [InlineData(OperatingState.Limited)]
    [InlineData(OperatingState.Maintenance)]
    public void Fault_is_reachable_from_operational_states(OperatingState from)
    {
        var result = StateMachine.Apply(from, StateTransitionTrigger.FaultDetected);
        Assert.True(result.Accepted);
        Assert.Equal(OperatingState.Fault, result.NewState);
    }

    [Fact]
    public void Fault_returns_to_ready_on_acknowledge()
    {
        var result = StateMachine.Apply(OperatingState.Fault, StateTransitionTrigger.Acknowledge);
        Assert.True(result.Accepted);
        Assert.Equal(OperatingState.Ready, result.NewState);
    }

    [Fact]
    public void Emergency_stop_returns_to_standby_on_acknowledge()
    {
        var result = StateMachine.Apply(OperatingState.EmergencyStop, StateTransitionTrigger.Acknowledge);
        Assert.True(result.Accepted);
        Assert.Equal(OperatingState.Standby, result.NewState);
    }

    [Fact]
    public void Maintenance_exits_to_standby()
    {
        var result = StateMachine.Apply(OperatingState.Maintenance, StateTransitionTrigger.ExitMaintenance);
        Assert.True(result.Accepted);
        Assert.Equal(OperatingState.Standby, result.NewState);
    }

    [Theory]
    [InlineData(OperatingState.Init, StateTransitionTrigger.Activate)]
    [InlineData(OperatingState.Standby, StateTransitionTrigger.BeginCharge)]
    [InlineData(OperatingState.Fault, StateTransitionTrigger.BeginCharge)]
    [InlineData(OperatingState.EmergencyStop, StateTransitionTrigger.BeginCharge)]
    [InlineData(OperatingState.EmergencyStop, StateTransitionTrigger.FaultDetected)]
    public void Disallowed_transitions_are_rejected(OperatingState from, StateTransitionTrigger trigger)
    {
        var result = StateMachine.Apply(from, trigger);
        Assert.False(result.Accepted);
        Assert.Equal(from, result.NewState);
    }
}
