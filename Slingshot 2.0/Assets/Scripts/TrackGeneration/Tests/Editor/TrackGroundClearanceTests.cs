using NUnit.Framework;

namespace TrackGeneration.Tests
{
    [Category("V2FastGate")]
    public sealed class TrackGroundClearanceTests
    {
        [Test]
        public void GroundLift_RaisesCompletedMeshToRequestedClearance()
        {
            Assert.AreEqual(12.05f, TrackGenerator.CalculateGroundLift(-12f, 0.05f), 0.0001f);
        }

        [Test]
        public void GroundLift_DoesNotMoveMeshThatAlreadyClearsGround()
        {
            Assert.AreEqual(0f, TrackGenerator.CalculateGroundLift(1f, 0.05f));
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void GroundLift_IgnoresInvalidMeasurements(float worldMinY)
        {
            Assert.AreEqual(0f, TrackGenerator.CalculateGroundLift(worldMinY));
        }
    }
}
