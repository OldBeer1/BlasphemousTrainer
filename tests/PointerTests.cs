using System;
using BlasphemousTrainer;
class PointerTests
{
    static int count;
    static void Check(bool ok, string name) { count++; if (!ok) { Console.WriteLine("FAIL: " + name); Environment.Exit(1); } }
    static void Main()
    {
        var click = new PointerClick();
        Check(click.Step(true, false, true, 2, 100, 200) == -1, "press does not execute");
        Check(click.Step(false, false, true, 2, 100, 200) == -1, "hold does not execute");
        Check(click.Step(false, true, false, 2, 100, 200) == 2, "release executes without IMGUI activation");
        Check(click.Step(false, true, false, 2, 100, 200) == -1, "duplicate release rejected");
        click.Step(true, false, true, 2, 100, 200);
        Check(click.Step(false, true, false, 3, 100, 200) == -1, "different button rejected");
        click.Step(true, false, true, -1, 100, 200);
        Check(click.Step(false, true, false, 2, 100, 200) == -1, "press outside then release inside rejected");
        click.Step(true, false, true, 2, 100, 200);
        Check(click.Step(false, true, false, -1, 100, 200) == -1, "disabled or clipped release rejected");
        click.Step(true, false, true, 2, 100, 200);
        Check(click.Step(false, false, true, 2, 120, 200) == -1, "drag does not execute");
        Check(click.Step(false, true, false, 2, 100, 200) == -1, "drag back does not click");
        click.Step(true, false, true, 2, 100, 200); click.Cancel();
        Check(click.Step(false, true, false, 2, 100, 200) == -1, "page/focus/close cancels click");
        click.Step(true, false, true, 2, 100, 200);
        Check(click.Step(false, true, false, 2, 103, 202) == 2, "small pointer jitter allowed");
        for (int i = 0; i < 100; i++) { click.Step(true,false,true,0,0,0); Check(click.Step(false,true,false,0,0,0)==0,"successive click "+i); }
        Console.WriteLine("PASS: " + count + " pointer activation checks.");
    }
}
