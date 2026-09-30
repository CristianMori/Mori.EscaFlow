PROGRAM P
VAR RETAIN
    Local : Outer;
END_VAR

Main();

ACTION Main
    O.In.Value := O.In.Value + 1.0;
    IF O.In.Bit1 AND Word.5 AND O.In.Flags[3] THEN
        Matrix[1, 2] := O.Count;
    END_IF;
    O.Items[1].Words[2] := Matrix[0, 1] + Local.Items[0].Words[3];
    O.Name := Label;
    O.Count := O.Short.LEN + O.Timer.PRE + O.Counter.ACC + O.Control.POS;
END_ACTION

ACTION Prescan (* hook: init *)
END_ACTION
END_PROGRAM

