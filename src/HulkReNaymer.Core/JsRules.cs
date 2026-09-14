using Jint;

namespace HulkReNaymer;

public static class JsRules
{
    public static (string? NewName, string Error) Run(string code, Dictionary<string, object?> context)
    {
        try
        {
            var engine = new Jint.Engine(options =>
            {
                options.TimeoutInterval(TimeSpan.FromMilliseconds(250));
                options.MaxStatements(10_000);
                options.LimitMemory(1_000_000);
            });
            foreach (var (key, value) in context)
                engine.SetValue(key, value ?? "");
            engine.Execute(code);
            var result = engine.GetValue("newName");
            return (result.IsString() || result.IsNumber() ? result.ToString() : context["newName"]?.ToString(), "");
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }
}
