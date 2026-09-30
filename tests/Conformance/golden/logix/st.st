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

T1.PRE := Rate;
TONR(T1); (* logix *)
Out := T1.DN;

ACTION Init (* hook: init *)
    TONR(T1); (* logix *)
END_ACTION
END_FUNCTION_BLOCK

PROGRAM P
VAR RETAIN
    InA : DINT;
    OutA : DINT;
END_VAR

Main();

ACTION Main
    D := TO_DINT#roundEven(R * 2.0);
    S := TO_SINT#wrap(D + 1);
    R := TO_REAL#roundEven(L);
    L := D / 3;
    IF D > 10 AND NOT Flag THEN
        D := 0;
    ELSIF D < 0 OR Flag THEN
        D := 10;
    ELSE
        D := D + 1;
    END_IF;
    CASE D OF
        1:
            Flag := 1;
        2..5, 8:
            Flag := 0;
        ELSE
            Flag := D.0;
    END_CASE;
    FOR I := 0 TO 4 BY 1 DO
        Arr[I] := I * 2;
        IF Arr[I] > 6 THEN
            EXIT;
        END_IF;
    END_FOR;
    WHILE I > 0 DO
        I := I - 1;
    END_WHILE;
    REPEAT
        I := I + 1;
    UNTIL I >= 3
    END_REPEAT;
    R := ABS(R) + SQRT(R) - SIN(R);
    D := TRUNC(R);
    Word.[I] := 1;
    Flag := Word.[D];
    D := Str.LEN;
    Str.DATA[0] := TO_SINT#wrap(65);
    Str2 := Str;
    IF TRUE THEN
        InA := D;
        Sub();
        Word := OutA;
    END_IF;
    Blk(Rate := 100, Out => Flag); (* Blink *)
    Flag := D >= 5 XOR Flag;
END_ACTION

ACTION Sub
    OutA := InA * 2;
    RETURN;
END_ACTION

ACTION Prescan (* hook: init *)
    Blk(); (* Blink, mode: prescan *)
END_ACTION
END_PROGRAM

