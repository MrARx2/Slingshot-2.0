using UnityEngine;

namespace Lunarlight.Hovercraft.V3.Diagnostics
{
    [DisallowMultipleComponent]
    public sealed class V3DiagnosticContactProbe : MonoBehaviour
    {
        private readonly ContactPoint[] contacts = new ContactPoint[64];
        private int contactCount;
        private int collisionCount;
        private Vector3 accumulatedNormal;
        private Vector3 accumulatedImpulse;
        private Vector3 accumulatedPoint;

        public void Consume(
            out int contactsThisTick,
            out int collisionsThisTick,
            out Vector3 averageNormal,
            out Vector3 totalImpulse,
            out Vector3 averagePoint)
        {
            contactsThisTick = contactCount;
            collisionsThisTick = collisionCount;
            averageNormal = accumulatedNormal.sqrMagnitude > 0.000001f
                ? accumulatedNormal.normalized
                : Vector3.zero;
            totalImpulse = accumulatedImpulse;
            averagePoint = contactCount > 0
                ? accumulatedPoint / contactCount : Vector3.zero;
            contactCount = 0;
            collisionCount = 0;
            accumulatedNormal = Vector3.zero;
            accumulatedImpulse = Vector3.zero;
            accumulatedPoint = Vector3.zero;
        }

        private void OnCollisionEnter(Collision collision)
        {
            Accumulate(collision);
        }

        private void OnCollisionStay(Collision collision)
        {
            Accumulate(collision);
        }

        private void Accumulate(Collision collision)
        {
            if (collision == null) return;
            collisionCount++;
            accumulatedImpulse += collision.impulse;
            int count = collision.GetContacts(contacts);
            contactCount += count;
            for (int i = 0; i < count; i++)
            {
                accumulatedNormal += contacts[i].normal;
                accumulatedPoint += contacts[i].point;
            }
        }
    }

    public sealed class V3WorldContactRecorder : IV3DiagnosticSource
    {
        private V3DiagnosticContext context;
        private V3DiagnosticContactProbe contactProbe;
        private V3HoverController hover;

        public string SourceId => "world_contact";
        public int SchemaVersion => V3DiagnosticSchema.Version;

        public void Initialize(V3DiagnosticContext value)
        {
            context = value;
            hover = value != null && value.Craft != null
                ? value.Craft.GetComponent<V3HoverController>()
                : null;
        }

        public void OnSessionStarted(V3DiagnosticSession session)
        {
            if (context != null && context.Craft != null)
            {
                contactProbe = context.Craft.GetComponent<V3DiagnosticContactProbe>();
                if (contactProbe == null)
                {
                    contactProbe = context.Craft.gameObject.AddComponent<V3DiagnosticContactProbe>();
                }
            }
        }

        public void Capture(ref V3DiagnosticSample sample)
        {
            int contactCount = 0;
            int collisionCount = 0;
            Vector3 normal = Vector3.zero;
            Vector3 impulse = Vector3.zero;
            Vector3 point = Vector3.zero;
            contactProbe?.Consume(out contactCount, out collisionCount,
                out normal, out impulse, out point);
            sample.world.contactCount = contactCount;
            sample.world.collisionCount = collisionCount;
            sample.world.contactImpulseNs = impulse.magnitude;
            sample.world.contactImpulseVectorNs = impulse;
            sample.world.averageContactNormal = normal;
            sample.world.averageContactPoint = point;

            sample.worldContactState = Classify(sample, contactCount);
        }

        public void OnSessionEnded(V3DiagnosticSession session)
        {
        }

        private V3WorldContactState Classify(
            V3DiagnosticSample sample, int contactCount)
        {
            if (contactCount > 0)
            {
                return V3WorldContactState.OnSurface;
            }
            if (sample.track.mapped && !sample.track.insideTrackBounds)
            {
                return V3WorldContactState.OutOfTrackBounds;
            }
            // World contact truth must not be derived from the craft's hover
            // belief. The independent track analyzer supplies the measured
            // surface relation; the hover controller is used only to obtain
            // the active build's envelope thresholds.
            V3HoverConfiguration configuration = hover != null
                ? hover.Configuration : null;
            float nearRange = configuration != null
                ? configuration.NearHoverRange : 0f;
            float captureRange = configuration != null
                ? configuration.CaptureRange : nearRange;
            bool hasIndependentSurfaceRelation = sample.track.mapped &&
                sample.track.trueSurfaceDistance >= 0f &&
                (sample.track.trueSurfaceNormal.sqrMagnitude > 0.1f ||
                 sample.track.normal.sqrMagnitude > 0.1f);
            if (hasIndependentSurfaceRelation &&
                sample.track.trueSurfaceDistance <= nearRange)
            {
                return V3WorldContactState.HoveringNearSurface;
            }
            if (hasIndependentSurfaceRelation &&
                sample.track.trueSurfaceDistance <= captureRange)
            {
                return V3WorldContactState.LosingSurface;
            }
            if (sample.track.expectedAirborne && sample.track.mapped)
            {
                return V3WorldContactState.AirborneExpected;
            }
            if (sample.track.mapped)
            {
                return V3WorldContactState.AirborneUnexpected;
            }
            return V3WorldContactState.Unknown;
        }
    }
}
