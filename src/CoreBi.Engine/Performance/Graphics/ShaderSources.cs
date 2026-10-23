namespace CoreBi.Engine.Performance.Graphics;

internal static class ShaderSources
{
    public static string Load(string name)
    {
        using var stream = typeof(ShaderSources).Assembly.GetManifestResourceStream($"CoreBi.Shaders.{name}.hlsl")
            ?? throw new InvalidOperationException($"Shader incorporado não encontrado: {name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
