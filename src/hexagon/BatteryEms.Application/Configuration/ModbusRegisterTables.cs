namespace BatteryEms.Application.Configuration;

// RM-M2-HIL-01: register-table label constants. The Modbus mapping
// schema enforces the enum {holding, input}; downstream adapters
// switch on these strings to pick the right read function code
// (FC03 for holding, FC04 for input). Default `Holding` keeps M1
// profiles compatible.
public static class ModbusRegisterTables
{
    public const string Holding = "holding";
    public const string Input = "input";
}

// RM-M2-HIL-01: 32-bit word-order labels. `HighLow` is the M1 default
// (high word first, matching the original RegisterDecoder); `LowHigh`
// is the HIL-side variant that swaps the two 16-bit words and is
// implemented by HIL-03.
public static class ModbusWordOrders
{
    public const string HighLow = "high_low";
    public const string LowHigh = "low_high";
}
