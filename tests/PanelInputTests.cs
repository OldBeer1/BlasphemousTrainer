using System;
using BlasphemousTrainer;
class PanelInputTests
{
    static int count;
    static void Check(bool ok, string label) { count++; if (!ok) { Console.WriteLine("FAIL: " + label); Environment.Exit(1); } }
    static void Main()
    {
        var repeat = new PanelRepeat();
        Check(repeat.Step(.2f, 0) == 0, "resting stick drift ignored");
        Check(repeat.Step(.59f, .1f) == 0, "partial movement below activation ignored");
        Check(repeat.Step(1, 1) == 1, "first navigation immediate");
        Check(repeat.Step(1, 1.3f) == 0, "initial repeat delay");
        Check(repeat.Step(1, 1.36f) == 1, "first repeat after delay");
        Check(repeat.Step(.4f, 1.4f) == 0, "hysteresis does not retrigger");
        Check(repeat.Step(.4f, 1.49f) == 1, "holding past release threshold keeps repeat");
        Check(repeat.Step(.25f, 1.5f) == 0, "release resets");
        Check(repeat.Step(-1, 1.51f) == -1, "new opposite direction immediate");
        Check(repeat.Step(1, 1.52f) == 1, "direct direction change immediate");
        repeat.Reset();
        Check(repeat.Step(1, 1.53f) == 1, "disconnect reset permits new action");
        Check(repeat.Step(1, 30) == 1, "large time gap emits one action not a backlog");
        Check(repeat.Step(1, 30) == 0, "same timestamp cannot repeat");
        var press = new PanelPress();
        Check(!press.Step(true), "held at connection cannot open");
        Check(!press.Step(false), "release arms right stick");
        Check(press.Step(true), "single right stick click opens immediately");
        Check(!press.Step(true), "holding right stick does not repeat");
        Check(!press.Step(true), "continued hold never closes or confirms");
        Check(!press.Step(false), "release rearms");
        Check(press.Step(true), "next single click works");
        press.Reset();
        Check(!press.Step(true), "focus or reconnect ignores held button");
        Check(!press.Step(false), "release after reset");
        Check(press.Step(true), "fresh click after reset");
        Check(PanelGrid.Move(0, 0, -1) == 2, "keypad row wraps left");
        Check(PanelGrid.Move(2, 0, 1) == 0, "keypad row wraps right");
        Check(PanelGrid.Move(2, 1, 0) == 5, "keypad down keeps column");
        Check(PanelGrid.Move(11, 1, 0) == 12, "last digit down reaches sign");
        Check(PanelGrid.Move(12, 0, 1) == 12, "sign row has one control");
        Check(PanelGrid.Move(12, 1, 0) == 13, "sign down reaches cancel");
        Check(PanelGrid.Move(13, 0, 1) == 14, "cancel right reaches done");
        Check(PanelGrid.Move(14, 0, 1) == 13, "done wraps to cancel");
        Check(PanelGrid.Move(0, -1, 0) == 13, "first row up reaches cancel");
        Check(PanelGrid.Move(14, 1, 0) == 1, "done down wraps to first row");
        Check(PanelGrid.Move(14, -1, 0) == 12, "done up reaches sign");
        Console.WriteLine("PASS: " + count + " panel input checks.");
    }
}
