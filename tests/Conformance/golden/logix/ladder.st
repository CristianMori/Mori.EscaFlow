FUNCTION_BLOCK Blink
VAR_INPUT
    EnableIn : BOOL;
    Rate : DINT;
END_VAR
VAR_OUTPUT
    EnableOut : BOOL;
    Out : BOOL;
END_VAR
VAR RETAIN
    T1 : TIMER;
END_VAR

VAR_TEMP
    _rc1 : BOOL;
END_VAR
_rc1 := NOT T1.DN;
T1(IN := _rc1); (* TON/logix *)
Out := NOT T1.DN AND T1.DN;

ACTION Init (* hook: init *)
    T1(IN := FALSE); (* TON/logix, mode: prescan *)
    Out := FALSE;
END_ACTION
END_FUNCTION_BLOCK

PROGRAM P
VAR RETAIN
    InA : DINT;
    InB : DINT;
    OutA : DINT;
END_VAR

Main();

ACTION Main
    VAR_TEMP
        _rc1 : BOOL;
        _rc2 : BOOL;
        _rc3 : BOOL;
        _mcr1 : BOOL;
        _rc4 : BOOL;
    END_VAR
    _rc1 := A AND NOT B;
    Out1 := _rc1;
    IF _rc1 THEN
        Out2 := TRUE;
    END_IF;
    IF _rc1 THEN
        Bits.3 := FALSE;
    END_IF;
    _rc2 := R_EDGE(A, Bits.0);
    Pulse := R_EDGE(_rc2, Bits.1);
    C := F_EDGE(_rc2, Bits.2);
    Out1 := X = Y AND X <> 1 AND X > 2 AND Y >= X AND Z < X AND Z <= Y AND X + Y > Z AND (0 <= 10 AND (0 <= X AND X <= 10) OR 0 > 10 AND (X >= 0 OR X <= 10)) AND (Bits AND 16#FF) = (Y AND 16#FF);
    T1(IN := A); (* TON/logix *)
    T2(IN := A); (* TOF/logix *)
    T3(IN := A); (* TONR/logix *)
    C1(CU := A); (* CTU/logix *)
    C2(CD := A); (* CTD/logix *)
    _rc3 := A AND B;
    IF _rc3 THEN
        T3.ACC := 0;
        T3.EN := FALSE;
        T3.TT := FALSE;
        T3.DN := FALSE;
    END_IF;
    IF _rc3 THEN
        C1.ACC := 0;
        C1.CU := FALSE;
        C1.CD := FALSE;
        C1.DN := FALSE;
        C1.OV := FALSE;
        C1.UN := FALSE;
    END_IF;
    IF A THEN
        Y := X;
    END_IF;
    IF A THEN
        Z := 0;
    END_IF;
    IF A THEN
        Z := X + Y;
    END_IF;
    IF A THEN
        Z := X - 1;
    END_IF;
    IF A THEN
        Z := X * 2;
    END_IF;
    IF A THEN
        Z := X / Y;
    END_IF;
    IF A THEN
        Z := X MOD 3;
    END_IF;
    IF A THEN
        Bits := X AND Y;
    END_IF;
    IF A THEN
        Bits := X OR 16#F;
    END_IF;
    IF A THEN
        Bits := Bits XOR Y;
    END_IF;
    IF A THEN
        Y := NOT X;
    END_IF;
    IF A THEN
        Z := -X;
    END_IF;
    IF A THEN
        R := ABS(R);
    END_IF;
    IF A THEN
        R := SQRT(R);
    END_IF;
    IF A THEN
        Z := TO_DINT#roundEven((X + Y) * 2 - R);
    END_IF;
    IF B THEN
        COPY(Src[0], Dst[0], 10); (* bytes *)
    END_IF;
    IF B THEN
        COPY_ATOMIC(Src[0], Dst[0], 10); (* bytes *)
    END_IF;
    IF B THEN
        FILL(0, Dst[0], 10);
    END_IF;
    IF A THEN
        InA := X;
        InB := Y;
        Sub();
        Z := OutA;
    END_IF;
    IF C THEN
        JMP Skip;
    END_IF;
    Out2 := A AND (B OR C AND (NOT A OR Out1) OR X = Y);
    Skip:
    _mcr1 := A;
    _rc4 := _mcr1 AND B;
    Out1 := _rc4;
    T1(IN := _rc4); (* TON/logix *)
    Out1 := FALSE;
    IF A THEN
        GSV(WallClockTime, , DateTime, Clock[0]); (* logix *)
    END_IF;
    IF A THEN
        SSV(Program, P, DisableFlag, X); (* logix *)
    END_IF;
    (* EN := A *) MSG(Msg1); (* logix *)
    Blk(EN := A, Rate := 250, Out => Out2); (* Blink *)
    IF A THEN
        Y := BIT_FIELD_DISTRIBUTE(X, 0, Y, 8, 4);
    END_IF;
    IF A THEN
        Bits := MASKED_MOVE(X, 16#FF00, Bits);
    END_IF;
    IF A THEN
        Y := SWAP_BYTES(X, REVERSE);
    END_IF;
    IF A THEN
        S3 := CONCAT(S1, S2);
    END_IF;
    IF A THEN
        S2 := MID(S1, 3, 1);
    END_IF;
    IF A THEN
        Idx := FIND(S1, S2, 1);
    END_IF;
    IF A THEN
        FOR Idx := 0 TO 9 BY 1 DO
            Loop();
        END_FOR;
    END_IF;
    IF B THEN
        RETURN;
    END_IF;
END_ACTION

ACTION Sub
    OutA := InA + InB;
    RETURN;
END_ACTION

ACTION Loop
    Dst[Idx] := Idx;
    IF Dst[Idx] = 5 THEN
        BRK(); (* logix *)
    END_IF;
END_ACTION

ACTION Prescan (* hook: init *)
    Out1 := FALSE;
    Bits.0 := TRUE;
    Pulse := FALSE;
    Bits.1 := TRUE;
    C := FALSE;
    Bits.2 := TRUE;
    Out1 := FALSE;
    T1(IN := FALSE); (* TON/logix, mode: prescan *)
    T2(IN := FALSE); (* TOF/logix, mode: prescan *)
    T3(IN := FALSE); (* TONR/logix, mode: prescan *)
    C1(CU := FALSE); (* CTU/logix, mode: prescan *)
    C2(CD := FALSE); (* CTD/logix, mode: prescan *)
    Out2 := FALSE;
    Out1 := FALSE;
    T1(IN := FALSE); (* TON/logix, mode: prescan *)
    Out1 := FALSE;
    MSG(Msg1); (* logix *)
    Blk(); (* Blink, mode: prescan *)
END_ACTION
END_PROGRAM

