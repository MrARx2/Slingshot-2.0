using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lunarlight.Hovercraft.V3
{
    [DisallowMultipleComponent]
    public sealed class V3SocketRegistry : MonoBehaviour
    {
        private static readonly IReadOnlyList<V3Socket> EmptySockets =
            Array.Empty<V3Socket>();

        private readonly List<V3Socket> sockets = new List<V3Socket>(16);
        private readonly Dictionary<string, V3Socket> socketsById =
            new Dictionary<string, V3Socket>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<V3Socket>> socketsByGroup =
            new Dictionary<string, List<V3Socket>>(StringComparer.Ordinal);

        public IReadOnlyList<V3Socket> Sockets => sockets;
        public int Count => sockets.Count;
        public bool IsValid { get; private set; }
        public string ValidationError { get; private set; } = string.Empty;

        public bool Initialize(IReadOnlyList<V3Socket> source)
        {
            sockets.Clear();
            socketsById.Clear();
            socketsByGroup.Clear();
            IsValid = true;
            ValidationError = string.Empty;

            if (source == null)
            {
                IsValid = false;
                ValidationError = "Socket source is missing.";
                return false;
            }

            for (int i = 0; i < source.Count; i++)
            {
                V3Socket socket = source[i];
                if (socket == null)
                {
                    IsValid = false;
                    ValidationError = "Socket source contains a null entry.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(socket.SocketId))
                {
                    IsValid = false;
                    ValidationError = "A runtime socket has no stable ID.";
                    return false;
                }

                if (!socketsById.TryAdd(socket.SocketId, socket))
                {
                    IsValid = false;
                    ValidationError =
                        $"Runtime socket ID '{socket.SocketId}' is duplicated.";
                    return false;
                }

                sockets.Add(socket);
                if (string.IsNullOrWhiteSpace(socket.ConfigurationGroup))
                {
                    continue;
                }

                if (!socketsByGroup.TryGetValue(
                    socket.ConfigurationGroup,
                    out List<V3Socket> group))
                {
                    group = new List<V3Socket>();
                    socketsByGroup.Add(socket.ConfigurationGroup, group);
                }

                group.Add(socket);
            }

            return true;
        }

        public bool TryGetSocket(string socketId, out V3Socket socket)
        {
            socket = null;
            return !string.IsNullOrEmpty(socketId) &&
                   socketsById.TryGetValue(socketId, out socket);
        }

        public bool TryGetPairedSocket(
            string socketId,
            out V3Socket pairedSocket)
        {
            pairedSocket = null;
            return TryGetSocket(socketId, out V3Socket socket) &&
                   !string.IsNullOrWhiteSpace(socket.PairedSocketId) &&
                   TryGetSocket(socket.PairedSocketId, out pairedSocket);
        }

        public IReadOnlyList<V3Socket> GetConfigurationGroup(
            string groupId)
        {
            return !string.IsNullOrEmpty(groupId) &&
                   socketsByGroup.TryGetValue(
                       groupId,
                       out List<V3Socket> group)
                ? group
                : EmptySockets;
        }
    }

    [DisallowMultipleComponent]
    public sealed class V3PartRegistry : MonoBehaviour
    {
        private static readonly IReadOnlyList<RuntimePartInstance> EmptyParts =
            Array.Empty<RuntimePartInstance>();

        private readonly List<RuntimePartInstance> parts =
            new List<RuntimePartInstance>(20);
        private readonly Dictionary<string, List<RuntimePartInstance>>
            partsBySocket =
                new Dictionary<string, List<RuntimePartInstance>>(
                    StringComparer.Ordinal);
        private readonly Dictionary<string, List<RuntimePartInstance>>
            partsByStableId =
                new Dictionary<string, List<RuntimePartInstance>>(
                    StringComparer.Ordinal);

        public IReadOnlyList<RuntimePartInstance> Parts => parts;
        public int Count => parts.Count;

        public void Initialize(IReadOnlyList<RuntimePartInstance> source)
        {
            parts.Clear();
            partsBySocket.Clear();
            partsByStableId.Clear();
            if (source == null)
            {
                return;
            }

            for (int i = 0; i < source.Count; i++)
            {
                RuntimePartInstance part = source[i];
                if (part == null || part.Definition == null)
                {
                    continue;
                }

                parts.Add(part);
                AddToIndex(partsBySocket, part.ParentSocketId, part);
                AddToIndex(
                    partsByStableId,
                    part.Definition.StableId,
                    part);
            }
        }

        public IReadOnlyList<RuntimePartInstance> GetPartsAtSocket(
            string socketId)
        {
            return !string.IsNullOrEmpty(socketId) &&
                   partsBySocket.TryGetValue(
                       socketId,
                       out List<RuntimePartInstance> matches)
                ? matches
                : EmptyParts;
        }

        public IReadOnlyList<RuntimePartInstance> GetPartsByStableId(
            string stablePartId)
        {
            return !string.IsNullOrEmpty(stablePartId) &&
                   partsByStableId.TryGetValue(
                       stablePartId,
                       out List<RuntimePartInstance> matches)
                ? matches
                : EmptyParts;
        }

        public bool TryGetConnector(
            string socketId,
            out RuntimePartInstance connector)
        {
            IReadOnlyList<RuntimePartInstance> matches =
                GetPartsAtSocket(socketId);
            for (int i = 0; i < matches.Count; i++)
            {
                if (matches[i].Definition is ConnectorDefinition)
                {
                    connector = matches[i];
                    return true;
                }
            }

            connector = null;
            return false;
        }

        public bool TryGetEndpoint(
            string socketId,
            out RuntimePartInstance endpoint)
        {
            IReadOnlyList<RuntimePartInstance> matches =
                GetPartsAtSocket(socketId);
            for (int i = 0; i < matches.Count; i++)
            {
                if (matches[i].Definition is EndpointDefinition)
                {
                    endpoint = matches[i];
                    return true;
                }
            }

            endpoint = null;
            return false;
        }

        public bool TryGetEndpoint<T>(
            string socketId,
            out T endpoint)
            where T : RuntimePartInstance
        {
            if (TryGetEndpoint(
                socketId,
                out RuntimePartInstance candidate) &&
                candidate is T typed)
            {
                endpoint = typed;
                return true;
            }

            endpoint = null;
            return false;
        }

        private static void AddToIndex(
            Dictionary<string, List<RuntimePartInstance>> index,
            string key,
            RuntimePartInstance part)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            if (!index.TryGetValue(
                key,
                out List<RuntimePartInstance> matches))
            {
                matches = new List<RuntimePartInstance>();
                index.Add(key, matches);
            }

            matches.Add(part);
        }
    }

    [DisallowMultipleComponent]
    public sealed class V3CapabilityRegistry : MonoBehaviour
    {
        private static readonly IReadOnlyList<RuntimePartInstance> EmptyParts =
            Array.Empty<RuntimePartInstance>();

        private readonly Dictionary<PartCapability, List<RuntimePartInstance>>
            providers =
                new Dictionary<
                    PartCapability,
                    List<RuntimePartInstance>>();

        public PartCapability ProvidedCapabilities { get; private set; }
        public PartCapability RequiredCapabilities { get; private set; }
        public PartCapability MissingCapabilities =>
            RequiredCapabilities & ~ProvidedCapabilities;
        public bool AreRequirementsSatisfied =>
            MissingCapabilities == PartCapability.None;

        public void Initialize(V3PartRegistry partRegistry)
        {
            providers.Clear();
            ProvidedCapabilities = PartCapability.None;
            RequiredCapabilities = PartCapability.None;
            if (partRegistry == null)
            {
                return;
            }

            for (int i = 0; i < partRegistry.Parts.Count; i++)
            {
                RuntimePartInstance part = partRegistry.Parts[i];
                PartDefinition definition = part.Definition;
                ProvidedCapabilities |= definition.ProvidedCapabilities;
                RequiredCapabilities |= definition.RequiredCapabilities;

                foreach (PartCapability capability in
                         EnumerateIndividualFlags(
                             definition.ProvidedCapabilities))
                {
                    if (!providers.TryGetValue(
                        capability,
                        out List<RuntimePartInstance> matches))
                    {
                        matches = new List<RuntimePartInstance>();
                        providers.Add(capability, matches);
                    }

                    matches.Add(part);
                }
            }
        }

        public bool HasAll(PartCapability capabilities)
        {
            return (ProvidedCapabilities & capabilities) == capabilities;
        }

        public bool HasAny(PartCapability capabilities)
        {
            return (ProvidedCapabilities & capabilities) != 0;
        }

        public bool HasOperationalProvider(PartCapability capability)
        {
            if (!IsIndividualFlag(capability) ||
                !providers.TryGetValue(
                    capability,
                    out List<RuntimePartInstance> matches))
            {
                return false;
            }

            for (int i = 0; i < matches.Count; i++)
            {
                RuntimePartInstance part = matches[i];
                if (part == null ||
                    part.Definition == null ||
                    !part.IsEnabled ||
                    part.IsThermallyLockedOut)
                {
                    continue;
                }

                if (!part.Definition.RequiresPower ||
                    part.GrantedPower + 0.0001f >=
                    Mathf.Max(0f, part.Definition.Power.idleDemand))
                {
                    return true;
                }
            }

            return false;
        }

        public IReadOnlyList<RuntimePartInstance> GetProviders(
            PartCapability capability)
        {
            return IsIndividualFlag(capability) &&
                   providers.TryGetValue(
                       capability,
                       out List<RuntimePartInstance> matches)
                ? matches
                : EmptyParts;
        }

        internal static IEnumerable<PartCapability> EnumerateIndividualFlags(
            PartCapability capabilities)
        {
            Array values = Enum.GetValues(typeof(PartCapability));
            for (int i = 0; i < values.Length; i++)
            {
                var capability = (PartCapability)values.GetValue(i);
                if (IsIndividualFlag(capability) &&
                    (capabilities & capability) != 0)
                {
                    yield return capability;
                }
            }
        }

        private static bool IsIndividualFlag(PartCapability capability)
        {
            int value = (int)capability;
            return value > 0 && (value & (value - 1)) == 0;
        }
    }
}
