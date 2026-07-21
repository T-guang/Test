using System.Collections.Generic;

namespace ElectricalSim.AI
{
    public enum CircuitRecognitionStatus { ExactMatch, NoMatch, Ambiguous }
    public enum CircuitRecognitionSource { LoadedTemplate, TopologyMatch, FreeBuilt }

    public sealed class CircuitRecognitionResult
    {
        public CircuitRecognitionStatus Status;
        public string MatchedTemplateId;
        public string MatchedTemplateName;
        public CircuitRecognitionSource Source;
        public string Reason;
        public int PrefilterCandidateCount;
        public int BacktrackSteps;
        public float ElapsedMilliseconds;
        public readonly List<string> CandidateTemplateIds = new List<string>();
    }
}
