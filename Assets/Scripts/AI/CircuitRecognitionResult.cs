using System.Collections.Generic;

namespace ElectricalSim.AI
{
    public enum CircuitRecognitionStatus
    {
        ExactMatch = 0,
        NoMatch = 1,
        Ambiguous = 2,
        EquivalentMatch = 3
    }
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
        public int EquivalentCandidateCount;
        public int EquivalentCheckCount;
        public float ElapsedMilliseconds;
        public readonly List<string> CandidateTemplateIds = new List<string>();
    }
}
