using Chasm.Core;

namespace Chasm.Core.Tests;

public class GameInfoTests
{
    [Fact]
    public void Canvas_is_16_by_9()
    {
        Assert.Equal(GameInfo.CanvasWidth * 9, GameInfo.CanvasHeight * 16);
    }
}
