PROGRAM P

Main();

ACTION Main
    Flag := DI:I.Data.4 AND NOT DI:I.Fault.0;
    ENet:O.Data[0] := Word;
    Word := ENet:I.Data[1] + DI:I.Data;
    Cfg := Turck:C.Channel_Mode_Port1;
    Byte0 := Turck:I.Data[0];
    Flag := Turck:I.ConnectionFaulted OR ENet:I.Data[1].0 OR DI:I.Data.4;
    DI:O.Data.0 := Flag;
END_ACTION

ACTION Prescan (* hook: init *)
END_ACTION
END_PROGRAM

