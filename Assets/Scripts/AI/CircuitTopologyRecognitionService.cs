using System.Collections.Generic;
using System.Diagnostics;
using ElectricalSim.Core;
using ElectricalSim.Templates;
using ElectricalSim.UI;
using UnityEngine;

namespace ElectricalSim.AI
{
    public sealed class CircuitTopologyRecognitionService
    {
        private const string CatalogPath = "Blueprints/Templates/template_catalog";
        private readonly List<CircuitTemplateCatalogItemDto> householdTemplates = new List<CircuitTemplateCatalogItemDto>();
        private bool initialized;

        public CircuitRecognitionResult Recognize(WorkspaceController workspace)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = new CircuitRecognitionResult { Status = CircuitRecognitionStatus.NoMatch, Source = TemplateEditSession.HasSystemTemplateLoaded ? CircuitRecognitionSource.LoadedTemplate : CircuitRecognitionSource.FreeBuilt };
            if (workspace == null || workspace.Components == null || workspace.WireManager == null)
            {
                result.Reason = "当前画布不可用。";
                return Finish(result, stopwatch);
            }

            EnsureCatalogLoaded();
            var graph = CircuitTopologyExtractor.FromWorkspace(workspace.Components, workspace.WireManager.Wires);
            var matches = new List<CircuitTemplateCatalogItemDto>();
            foreach (var item in householdTemplates)
            {
                if (!CircuitTemplateLoader.TryLoad(item.resourcePath, out var template, out _)) continue;
                var templateGraph = CircuitTopologyExtractor.FromTemplate(template);
                if (!CircuitTopologyMatcher.PassesPrefilter(graph, templateGraph)) continue;
                result.PrefilterCandidateCount++;
                if (CircuitTopologyMatcher.IsExactMatch(graph, templateGraph, out var steps))
                {
                    result.BacktrackSteps += steps;
                    matches.Add(item);
                }
            }

            if (matches.Count == 1)
            {
                var match = matches[0];
                result.Status = CircuitRecognitionStatus.ExactMatch;
                result.MatchedTemplateId = match.templateId;
                result.MatchedTemplateName = match.templateName;
                result.Source = TemplateEditSession.HasSystemTemplateLoaded && TemplateEditSession.CurrentTemplateId == match.templateId
                    ? CircuitRecognitionSource.LoadedTemplate : CircuitRecognitionSource.TopologyMatch;
                result.Reason = "当前静态端子拓扑与标准模板完全一致。";
            }
            else if (matches.Count > 1)
            {
                result.Status = CircuitRecognitionStatus.Ambiguous;
                foreach (var match in matches) result.CandidateTemplateIds.Add(match.templateId);
                result.Reason = "多个标准模板具有完全相同的静态拓扑。";
            }
            else
            {
                result.Reason = TemplateEditSession.HasSystemTemplateLoaded
                    ? "当前拓扑已偏离原始系统模板。" : "未匹配到标准家庭电路。";
            }

            return Finish(result, stopwatch);
        }

        private void EnsureCatalogLoaded()
        {
            if (initialized) return;
            initialized = true;
            var asset = Resources.Load<TextAsset>(CatalogPath);
            var catalog = asset != null ? JsonUtility.FromJson<CircuitTemplateCatalogDto>(asset.text) : null;
            if (catalog == null || catalog.templates == null) return;
            foreach (var item in catalog.templates)
            {
                if (item != null && item.category == "家庭电路") householdTemplates.Add(item);
            }
        }

        private static CircuitRecognitionResult Finish(CircuitRecognitionResult result, Stopwatch stopwatch)
        {
            stopwatch.Stop();
            result.ElapsedMilliseconds = (float)stopwatch.Elapsed.TotalMilliseconds;
            return result;
        }
    }
}
