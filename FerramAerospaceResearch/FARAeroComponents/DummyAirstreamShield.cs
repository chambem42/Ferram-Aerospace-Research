using System.Diagnostics.CodeAnalysis;

namespace FerramAerospaceResearch.FARAeroComponents
{
    public sealed class DummyAirstreamShield : IAirstreamShield
    {
        /// <inheritdoc />
        public bool ClosedAndLocked()
        {
            return true;
        }

        public Part part;

        /// <inheritdoc />
        public Vessel GetVessel()
        {
            return part.vessel;
        }

        /// <inheritdoc />
        // coverage: IAirstreamShield member KSP never calls
        [ExcludeFromCodeCoverage]
        public Part GetPart()
        {
            return part;
        }
    }
}
