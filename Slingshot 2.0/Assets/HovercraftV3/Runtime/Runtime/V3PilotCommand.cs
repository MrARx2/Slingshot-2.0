using System;

namespace Lunarlight.Hovercraft.V3
{
    [Serializable]
    public struct V3PilotCommand
    {
        public float Throttle;
        public float Strafe;
        public float Yaw;
        public float Pitch;
        public float Lift;
        public float Downforce;
        public bool StabilizationEnabled;
        public bool GripBreaker;
        public bool EmergencyOverload;
        public bool CyclePowerAllocationMode;
    }
}
