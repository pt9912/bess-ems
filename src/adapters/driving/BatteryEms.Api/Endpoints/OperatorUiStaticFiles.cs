using Microsoft.AspNetCore.Builder;

namespace BatteryEms.Api.Endpoints;

internal static class OperatorUiStaticFiles
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
