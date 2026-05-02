namespace Unity2Snap.Editor
{
    internal sealed class UslsWarningSink
    {
        private readonly UslsManifest manifest;
        private int counter;

        public UslsWarningSink(UslsManifest manifest)
        {
            this.manifest = manifest;
        }

        public void Add(string severity, string code, string objectId, string objectPath, string message, string recommendation)
        {
            counter++;
            manifest.warnings.Add(new UslsWarning
            {
                id = "warn_" + counter.ToString("0000"),
                severity = severity,
                code = code,
                objectId = objectId,
                objectPath = objectPath,
                message = message,
                recommendation = recommendation
            });
        }
    }
}
