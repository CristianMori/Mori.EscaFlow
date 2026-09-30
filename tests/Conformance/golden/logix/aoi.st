FUNCTION_BLOCK Motor
VAR_INPUT
    EnableIn : BOOL;
    Cmd : BOOL; (* run command *)
    Speed : REAL;
    Delay : DINT := 500;
END_VAR
VAR_OUTPUT
    EnableOut : BOOL;
    Running : BOOL;
    Fault : BOOL;
END_VAR
VAR_IN_OUT
    Data : MotorData;
END_VAR
VAR RETAIN
    T1 : TIMER;
    Count : DINT;
    Prev : BOOL;
END_VAR

VAR_TEMP
    _rc1 : BOOL;
END_VAR
T1(IN := Cmd); (* TON/logix *)
Running := Cmd AND T1.DN;
_rc1 := R_EDGE(Cmd, Prev);
IF _rc1 THEN
    Data.Starts := Data.Starts + 1;
END_IF;
IF _rc1 THEN
    Count := Count + 1;
END_IF;
Fault := Running AND Speed > 100.0;

ACTION Prescan
    Running := 0;
    Count := 0;
END_ACTION

ACTION Postscan (* hook: postscan *)
    Running := 0;
END_ACTION

ACTION EnableInFalse (* hook: enableFalse *)
    Running := 0;
    Fault := 0;
END_ACTION

ACTION Init (* hook: init *)
    T1(IN := FALSE); (* TON/logix, mode: prescan *)
    Running := FALSE;
    Prev := TRUE;
    Fault := FALSE;
    Prescan();
END_ACTION
END_FUNCTION_BLOCK

PROGRAM P

Main();

ACTION Main
    M1(EN := Start, Cmd := Start, Speed := Spd, Data := MD1, Running => Run1); (* Motor *)
    Text();
END_ACTION

ACTION Text
    M2(Cmd := Start, Speed := Spd, Data := MD2, Running => Run2); (* Motor *)
    M2.Delay := 250;
    Run2 := M2.Running AND NOT M2.Fault;
END_ACTION

ACTION Prescan (* hook: init *)
    M1(Data := MD1); (* Motor, mode: prescan *)
    M2(Data := MD2); (* Motor, mode: prescan *)
END_ACTION
END_PROGRAM

