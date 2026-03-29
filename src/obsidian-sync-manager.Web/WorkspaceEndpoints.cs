namespace Swick.Obsidian.SyncManager.Web;

public static class WorkspaceEndpoints
{
    extension(WebApplication app)
    {
        public WebApplication MapWorkspaceApi()
        {
            var group = app.MapGroup("/api/workspaces").RequireAuthorization();

            group.MapGet("/", async (WorkspaceService workspaces, HttpContext context) =>
            {
                var (username, _) = GetUserClaims(context);
                return await workspaces.ListAsync(username);
            });

            group.MapPost("/", async (CreateWorkspaceRequest request, WorkspaceService workspaces, HttpContext context) =>
            {
                if (!WorkspaceService.IsValidWorkspaceName(request.Name))
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["name"] = ["Must be 1-64 lowercase alphanumeric characters or hyphens, starting with a letter or digit."]
                    });

                var (username, sub) = GetUserClaims(context);
                var (success, workspace) = await workspaces.CreateAsync(username, request.Name);

                return success
                    ? Results.Created($"/api/workspaces/{workspace!.Id}", workspace)
                    : Results.Conflict(new { message = "Workspace already exists." });
            });

            group.MapDelete("/{id}", async (string id, WorkspaceService workspaces, HttpContext context) =>
            {
                var (username, _) = GetUserClaims(context);
                return await workspaces.DeleteAsync(id, username) ? Results.NoContent() : Results.NotFound();
            });

            group.MapPost("/{id}/setup-uri", async (string id, WorkspaceService workspaces, HttpContext context) =>
            {
                var (username, sub) = GetUserClaims(context);
                var workspace = await workspaces.GetAsync(id);
                if (workspace is null || !workspace.Members.Contains(username))
                    return Results.NotFound();

                var result = workspaces.GenerateSetupUri(username, sub, id, workspace.DatabaseName, workspace.E2eePassphrase);
                return Results.Ok(new { setupUri = result.Uri, uriPassphrase = result.UriPassphrase, e2eePassphrase = result.E2eePassphrase });
            });

            group.MapGet("/{id}/members", async (string id, WorkspaceService workspaces, HttpContext context) =>
            {
                var (username, _) = GetUserClaims(context);
                var workspace = await workspaces.GetAsync(id);
                if (workspace is null || !workspace.Members.Contains(username))
                    return Results.NotFound();

                return Results.Ok(workspace.Members);
            });

            group.MapPost("/{id}/members", async (AddMemberRequest request, string id, WorkspaceService workspaces, HttpContext context) =>
            {
                var (username, _) = GetUserClaims(context);
                return await workspaces.AddMemberAsync(id, username, request.Username)
                    ? Results.Ok()
                    : Results.BadRequest(new { message = "Could not add member. Ensure you are a member of this workspace and the target user exists." });
            });

            group.MapDelete("/{id}/members/{targetUsername}", async (string id, string targetUsername, WorkspaceService workspaces, HttpContext context) =>
            {
                var (username, _) = GetUserClaims(context);
                return await workspaces.RemoveMemberAsync(id, username, targetUsername)
                    ? Results.NoContent()
                    : Results.BadRequest(new { message = "Could not remove member. You cannot remove the last member." });
            });

            return app;
        }
    }

    private static (string Username, string Sub) GetUserClaims(HttpContext context)
    {
        var sub = context.User.FindFirst("sub")?.Value
            ?? throw new InvalidOperationException("Authenticated user has no sub claim.");
        var username = context.User.FindFirst("preferred_username")?.Value
            ?? throw new InvalidOperationException("Authenticated user has no preferred_username claim.");
        return (username, sub);
    }
}

record CreateWorkspaceRequest(string Name);
record AddMemberRequest(string Username);
public record WorkspaceInfo(string Id, string Name, string DatabaseName, List<string> Members, string E2eePassphrase);
