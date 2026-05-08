namespace CSharpFar.App.HitTesting;

public interface IHitTestable
{
    HitTestResult HitTest(int x, int y);
}
