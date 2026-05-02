using System.Collections.Generic;
using System.Text;

namespace Unity2Snap.Editor
{
    internal static class UslsReportWriter
    {
        public static string CreateReport(UslsManifest manifest)
        {
            var builder = new StringBuilder();
            builder.AppendLine("# Unity2Snap Export Report");
            builder.AppendLine();
            builder.AppendLine("## Scene");
            builder.AppendLine();
            builder.AppendLine("- Scene: `" + manifest.exporter.sceneName + "`");
            builder.AppendLine("- Scene path: `" + manifest.exporter.scenePath + "`");
            builder.AppendLine("- Unity version: `" + manifest.exporter.unityVersion + "`");
            builder.AppendLine("- Exported UTC: `" + manifest.exporter.exportedAtUtc + "`");
            builder.AppendLine("- USLS schema: `" + manifest.version + "`");
            builder.AppendLine();
            builder.AppendLine("## Summary");
            builder.AppendLine();
            builder.AppendLine("- Objects: " + manifest.stats.objectCount);
            builder.AppendLine("- Mesh objects: " + manifest.stats.meshObjectCount);
            builder.AppendLine("- Primitive objects: " + manifest.stats.primitiveObjectCount);
            builder.AppendLine("- Lights: " + manifest.stats.lightCount);
            builder.AppendLine("- Camera hints: " + manifest.stats.cameraHintCount);
            builder.AppendLine("- Colliders: " + manifest.stats.colliderCount);
            builder.AppendLine("- Player spawns: " + manifest.stats.playerSpawnCount);
            builder.AppendLine("- Materials: " + manifest.stats.materialCount);
            builder.AppendLine("- Textures: " + manifest.stats.textureCount);
            builder.AppendLine("- Total triangles: " + manifest.stats.totalTriangles);
            builder.AppendLine("- Warnings: " + manifest.stats.warningCount);
            builder.AppendLine("- Notices: " + manifest.stats.noticeCount);
            builder.AppendLine("- Errors: " + manifest.stats.errorCount);
            builder.AppendLine();
            builder.AppendLine("## Lens Studio Notes");
            builder.AppendLine();
            builder.AppendLine("- Positions are exported in `" + manifest.coordinateSystem.targetUnit + "`.");
            builder.AppendLine("- Rotation is exported as local Euler degrees after the configured handedness conversion.");
            builder.AppendLine("- Parent relationships should be created before applying local transforms.");
            builder.AppendLine("- `player_spawn` should offset the imported scene root; it should not move the device tracking origin.");
            builder.AppendLine("- For Spectacles, export the XR/VR rig root or scene root so parent transforms are preserved before the Lens root offset is applied.");
            builder.AppendLine();

            if (manifest.stats.totalTriangles > 100000)
            {
                builder.AppendLine("## Performance Risk");
                builder.AppendLine();
                builder.AppendLine("- Total exported triangles exceed 100,000. This is a high-risk scene for Lens/Spectacles without optimization.");
                builder.AppendLine();
            }

            builder.AppendLine("## Issue Breakdown");
            builder.AppendLine();

            if (manifest.warnings.Count == 0)
            {
                builder.AppendLine("No warnings or notices.");
                return builder.ToString();
            }

            AppendIssueBreakdown(builder, manifest);
            builder.AppendLine();
            builder.AppendLine("## Warnings And Errors");
            builder.AppendLine();

            var wroteWarning = false;
            for (var i = 0; i < manifest.warnings.Count; i++)
            {
                var warning = manifest.warnings[i];
                if (warning.severity == "info")
                {
                    continue;
                }

                wroteWarning = true;
                builder.AppendLine("### " + warning.id + " `" + warning.code + "`");
                builder.AppendLine();
                builder.AppendLine("- Severity: `" + warning.severity + "`");
                builder.AppendLine("- Object: `" + warning.objectPath + "`");
                builder.AppendLine("- Message: " + warning.message);
                builder.AppendLine("- Recommendation: " + warning.recommendation);
                builder.AppendLine();
            }

            if (!wroteWarning)
            {
                builder.AppendLine("No warnings or errors.");
                builder.AppendLine();
            }

            builder.AppendLine("## Notices");
            builder.AppendLine();
            builder.AppendLine("Informational notices are summarized above and kept out of the detailed warning list so the report stays readable.");

            return builder.ToString();
        }

        private static void AppendIssueBreakdown(StringBuilder builder, UslsManifest manifest)
        {
            var counts = new Dictionary<string, int>();
            for (var i = 0; i < manifest.warnings.Count; i++)
            {
                var warning = manifest.warnings[i];
                var key = warning.severity + " / " + warning.code;
                if (!counts.ContainsKey(key))
                {
                    counts[key] = 0;
                }

                counts[key]++;
            }

            foreach (var pair in counts)
            {
                builder.AppendLine("- `" + pair.Key + "`: " + pair.Value);
            }
        }
    }
}
