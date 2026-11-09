using Chasm.Game.Scenes;
using Microsoft.Xna.Framework.Graphics;

namespace Chasm.Tools.Scenes;

/// <summary>Placeholder until the combat system lands.</summary>
public sealed class CombatSandboxScene : Scene
{
    public override void OnEnter() => ToolContext.EnsureInitialized(Services);
    public override void Update(float dt) { ToolContext.MenuBar(Services, "Combat Sandbox (coming)"); ToolContext.Wizards.Draw(); }
    public override void Draw(SpriteBatch batch) { }
}
