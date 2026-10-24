using Catalog.Api.Contracts;
using Catalog.Api.Domain;
using Catalog.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Api.Endpoints;

/// <summary>Catégories du catalogue (basket, figurines, posters...) — créées uniquement par l'admin (exigence contexte).</summary>
public static class CategoryEndpoints
{
    public static void MapCategoryEndpoints(this IEndpointRouteBuilder app)
    {
        // Public : nécessaire pour le parcours catalogue (filtre) et le formulaire de dépôt d'article,
        // ni l'un ni l'autre n'exige d'être authentifié avant l'achat/la vente elle-même.
        app.MapGet("/categories", async (CatalogDbContext db) =>
            await db.Categories.OrderBy(c => c.Name).ToListAsync());

        app.MapPost("/categories", async (CreateCategoryRequest request, CatalogDbContext db) =>
        {
            if (await db.Categories.AnyAsync(c => c.Name == request.Name))
            {
                return Results.Conflict(new { message = "Une catégorie avec ce nom existe déjà." });
            }

            Category category = new() { Name = request.Name };
            db.Categories.Add(category);
            await db.SaveChangesAsync();
            return Results.Created($"/categories/{category.Id}", category);
        }).RequireAuthorization(policy => policy.RequireRole("Admin"));
    }
}
