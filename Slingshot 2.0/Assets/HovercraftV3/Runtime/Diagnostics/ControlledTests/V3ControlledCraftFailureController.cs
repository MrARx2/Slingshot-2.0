using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3.Diagnostics.ControlledTests
{
    /// <summary>
    /// Test-runner-owned adapter for production part availability state. It
    /// never disables controller scripts or injects force; accepted failures
    /// go through RuntimePartInstance.SetEnabled and are restored at cleanup.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class V3ControlledCraftFailureController : MonoBehaviour,
        IV3ControlledFailurePathTarget
    {
        private readonly List<RuntimePartInstance> changedParts =
            new List<RuntimePartInstance>(8);
        private V3CraftRuntime craft;

        public void Initialize(V3CraftRuntime runtime)
        {
            craft = runtime;
        }

        public bool TryApplyControlledFailure(
            V3ControlledFailurePathRequest request,
            out string evidence)
        {
            evidence = string.Empty;
            if (craft == null || request == null)
                return false;

            switch (request.failureMode)
            {
                case V3ControlledFailureMode.DeviceUnavailable:
                case V3ControlledFailureMode.DeviceDamaged:
                case V3ControlledFailureMode.ActuatorHeldNeutral:
                    return DisableMatchingPart(
                        request,
                        part => !(part.Definition is V3SystemComputerDefinition),
                        out evidence);
                case V3ControlledFailureMode.ComputerOffline:
                    return DisableMatchingPart(
                        request,
                        part => part.Definition is V3SystemComputerDefinition,
                        out evidence);
                case V3ControlledFailureMode.SystemsPowerDenied:
                    return DisableMatchingPart(
                        request,
                        part => part.Definition is EnergyCoreDefinition,
                        out evidence);
                default:
                    // Software/firmware/rate failures need a first-class
                    // scheduler or firmware fault API. Refuse to impersonate
                    // those states by disabling arbitrary behaviours.
                    return false;
            }
        }

        public void ClearControlledFailures()
        {
            for (int i = 0; i < changedParts.Count; i++)
                if (changedParts[i] != null)
                    changedParts[i].SetEnabled(true);
            changedParts.Clear();
        }

        private bool DisableMatchingPart(
            V3ControlledFailurePathRequest request,
            Predicate<RuntimePartInstance> domain,
            out string evidence)
        {
            string target = request.targetId ?? string.Empty;
            for (int i = 0; i < craft.InstalledParts.Count; i++)
            {
                RuntimePartInstance part = craft.InstalledParts[i];
                if (part == null || !domain(part) || !Matches(part, target))
                    continue;
                if (part.IsEnabled)
                {
                    part.SetEnabled(false);
                    changedParts.Add(part);
                }
                evidence = "Normal part-availability path set `" +
                    part.ParentSocketId + "` / `" +
                    (part.Definition != null
                        ? part.Definition.StableId
                        : string.Empty) + "` offline for " +
                    request.failureMode + ".";
                return true;
            }
            evidence = string.Empty;
            return false;
        }

        private static bool Matches(RuntimePartInstance part, string target)
        {
            if (string.IsNullOrWhiteSpace(target) || target == "*")
                return true;
            if (string.Equals(part.ParentSocketId, target,
                StringComparison.OrdinalIgnoreCase))
                return true;
            PartDefinition definition = part.Definition;
            if (definition != null && string.Equals(
                definition.StableId,
                target,
                StringComparison.OrdinalIgnoreCase))
                return true;
            if (definition is V3SystemComputerDefinition computer &&
                string.Equals(computer.ComputerRole.ToString(), target,
                    StringComparison.OrdinalIgnoreCase))
                return true;
            return false;
        }
    }
}
