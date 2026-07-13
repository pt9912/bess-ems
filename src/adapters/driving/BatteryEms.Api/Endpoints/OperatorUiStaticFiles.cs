using Microsoft.AspNetCore.Builder;

namespace BatteryEms.Api.Endpoints;

// Public so the production-shaped BatteryEms.Host composition can wire the
// same operator shell as the standalone API host — the shell assets are
// published into the runtime image either way (RM-M6-01-B).
public static class OperatorUiStaticFiles
{
    public static WebApplication UseOperatorUiStaticShell(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.Equals("/operator", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.Redirect("/operator/");
                return;
            }

            await next().ConfigureAwait(false);
        });

        app.UseDefaultFiles();
        app.UseStaticFiles();
        return app;
    }
}
