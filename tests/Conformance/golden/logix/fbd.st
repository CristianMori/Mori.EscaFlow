FUNCTION_BLOCK Blink
VAR_INPUT
    EnableIn : BOOL;
    Rate : DINT;
    Reset : BOOL;
END_VAR
VAR_OUTPUT
    EnableOut : BOOL;
    Out : BOOL;
END_VAR
VAR_IN_OUT
    Data : DINT;
END_VAR
VAR RETAIN
    T1 : TIMER;
END_VAR

T1.PRE := Rate;
Out := T1.DN AND NOT Reset;
Data := Data + 1;

ACTION Init (* hook: init *)
END_ACTION
END_FUNCTION_BLOCK

PROGRAM P
VAR RETAIN
    InA : DINT;
    OutA : DINT;
END_VAR

Main();

ACTION Main
    VAR_TEMP
        _f1 : REAL;
    END_VAR
    TONR_01.TimerEnable := In1;
    TONR_01.PRE := Preset;
    TONR_01.Reset := Blink_01.Out;
    TONR(TONR_01); (* logix *)
    Out1 := TONR_01.DN;
    Blink_01.Rate := Preset;
    Blink_01.Reset := TONR_01.DN;
    Blink_01(Data := Counter); (* Blink *)
    Out2 := Blink_01.Out;
    _f1 := Val + 1.5;
    SumOut := _f1;
    InA := Preset;
    Sub();
    RetVal := OutA;
END_ACTION

ACTION Sub
    OutA := InA + 1;
    RETURN;
END_ACTION

ACTION Prescan (* hook: init *)
    TONR(TONR_01); (* logix *)
    Blink_01(); (* Blink, mode: prescan *)
END_ACTION
END_PROGRAM

