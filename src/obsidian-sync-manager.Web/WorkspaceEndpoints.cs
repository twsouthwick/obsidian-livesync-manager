namespace obsidian_sync_manager.Web;

public static class WorkspaceEndpoints
{
    extension(WebApplication app)
    {
        public WebApplication MapWorkspaceApi()
        {
            var group = app.MapGroup("/api/workspaces").RequireAuthorization();

            group.MapGet("/", async (WorkspaceService workspaces, HttpContext context) =>
            {
                var userId = GetUserId(context);
                return await workspaces.ListAsync(userId);
            });

            group.MapPost("/", async (CreateWorkspaceRequest request, WorkspaceService workspaces, HttpContext context) =>
            {
                if (!WorkspaceService.IsValidWorkspaceName(request.Name))
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["name"] = ["Must be 1-64 lowercase alphanumeric characters or hyphens, starting with a letter or digit."]
                    });

                var userId = GetUserId(context);
                var (success, workspace) = await workspaces.CreateAsync(userId, request.Name);

                return success
                    ? Results.Created($"/api/workspaces/{request.Name}", workspace)
                    : Results.Conflict(new { message = "Workspace already exists." });
            });

            group.MapDelete("/{name}", async (string name, WorkspaceService workspaces, HttpContext context) =>
            {
                if (!WorkspaceService.IsValidWorkspaceName(name))
                    return Results.BadRequest();

                var userId = GetUserId(context);
                return await workspaces.DeleteAsync(userId, name) ? Results.NoContent() : Results.NotFound();
            });

            group.MapPost("/{name}/setup-uri", (string name, WorkspaceService workspaces, HttpContext context) =>
            {
                if (!WorkspaceService.IsValidWorkspaceName(name))
                    return Results.BadRequest();

                var userId = GetUserId(context);
                var result = workspaces.GenerateSetupUri(userId, name);
                return Results.Ok(new { setupUri = result.Uri, uriPassphrase = result.UriPassphrase, e2eePassphrase = result.E2eePassphrase });
            });

            return app;
        }
    }

    private static string GetUserId(HttpContext context) =>
        context.User.FindFirst("sub")?.Value
            ?? throw new InvalidOperationException("Authenticated user has no sub claim.");
}

record CreateWorkspaceRequest(string Name);
public record WorkspaceInfo(string Name, string DatabaseName);
