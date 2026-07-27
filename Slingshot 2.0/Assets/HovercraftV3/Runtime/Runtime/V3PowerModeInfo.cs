namespace Lunarlight.Hovercraft.V3
{
    /// <summary>
    /// Player-facing descriptions for the power allocator's priority modes.
    /// </summary>
    public static class V3PowerModeInfo
    {
        public static string GetShortDescription(
            V3PowerAllocationMode mode)
        {
            switch (mode)
            {
                case V3PowerAllocationMode.Propulsion:
                    return "Drive thrust first; stabilization yields under shortage.";
                case V3PowerAllocationMode.Stability:
                    return "Hover and stabilization first; acceleration yields.";
                case V3PowerAllocationMode.Recovery:
                    return "Essential hover first, then drive; steering yields.";
                default:
                    return "All-round priority for predictable normal driving.";
            }
        }

        public static string GetPriorityOrder(
            V3PowerAllocationMode mode)
        {
            switch (mode)
            {
                case V3PowerAllocationMode.Propulsion:
                    return "Critical > Drive > Steering > Stability";
                case V3PowerAllocationMode.Stability:
                    return "Stability > Critical > Steering > Drive";
                case V3PowerAllocationMode.Recovery:
                    return "Critical > Stability > Drive > Steering";
                default:
                    return "Critical > Stability > Steering > Drive";
            }
        }

        public static string GetLimitationNote(bool isPowerLimited)
        {
            return isPowerLimited
                ? "Power is constrained: the selected priority is active."
                : "Power headroom available: modes may feel identical until demand exceeds supply.";
        }
    }
}
