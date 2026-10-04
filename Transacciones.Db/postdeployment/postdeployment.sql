-- Datos iniciales. Corre al desplegar, DESPUES de todos los objetos del
-- proyecto, asi que es el sitio natural para sembrar catalogos.
--
-- MERGE y no "IF NOT EXISTS INSERT": la segunda forma falla si el INSERT se
-- ejecuta dos veces. MERGE hace las dos cosas.
MERGE [dbo].[EstadoSolicitud] AS destino
USING (VALUES
    (N'Borrador',   0, N'Solicitud creada, aun no enviada',  0),
    (N'Registrada', 1, N'Registrada y pendiente de validacion', 0),
    (N'Aprobada',   2, N'Aprobada por el flujo correspondiente', 0),
    (N'Rechazada',  3, N'Rechazada. Estado terminal',            1),
    (N'Vencida',    4, N'No atendida antes del vencimiento',    1),
    (N'Anulada',    5, N'Anulada por el usuario. Estado terminal', 1)
) AS origen ([Codigo], [Valor], [Descripcion], [EsTerminal])
    ON destino.[Codigo] = origen.[Codigo]
WHEN MATCHED THEN
    UPDATE SET [Valor] = origen.[Valor],
               [Descripcion] = origen.[Descripcion],
               [EsTerminal] = origen.[EsTerminal]
WHEN NOT MATCHED THEN
    INSERT ([Codigo], [Valor], [Descripcion], [EsTerminal])
    VALUES (origen.[Codigo], origen.[Valor], origen.[Descripcion], origen.[EsTerminal]);