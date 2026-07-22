using System;
using System.Reflection;
using ElectricalSim.Templates;
using UnityEditor;
using UnityEngine;

namespace ElectricalSim.Editor
{
    public static class MeterLampTemplateRouteValidationTests
    {
        [MenuItem("Tools/Tests/Run Meter Lamp Template Route Validation Tests")]
        public static void RunTests()
        {
            var asset = Resources.Load<TextAsset>("Blueprints/Templates/meter_lamp_template");
            if (asset == null)
            {
                throw new InvalidOperationException("meter_lamp_template could not be loaded.");
            }

            var template = JsonUtility.FromJson<CircuitTemplateDto>(asset.text);
            if (template == null || template.wires == null || template.wires.Count != 7)
            {
                throw new InvalidOperationException("meter_lamp_template wire data is incomplete.");
            }

            var validator = typeof(CircuitTemplateSpawnService).GetMethod(
                "ValidateManualRoutePoints",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (validator == null)
            {
                throw new InvalidOperationException("CircuitTemplateSpawnService.ValidateManualRoutePoints was not found.");
            }

            for (var i = 0; i < template.wires.Count; i++)
            {
                var arguments = new object[] { template.wires[i].manualRoutePoints, null };
                var valid = (bool)validator.Invoke(null, arguments);
                if (!valid)
                {
                    throw new InvalidOperationException("meter_lamp_template wire " + i + " was rejected: " + arguments[1]);
                }
            }

            Debug.Log("Meter lamp template route validation tests: 7/7 wires accepted.");
        }
    }
}
