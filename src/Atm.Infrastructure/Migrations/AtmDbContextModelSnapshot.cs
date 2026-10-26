using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Atm.Infrastructure.Migrations;

[DbContext(typeof(AtmDbContext))]
public sealed class AtmDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder) =>
        InitialModel.Build(modelBuilder);
}
