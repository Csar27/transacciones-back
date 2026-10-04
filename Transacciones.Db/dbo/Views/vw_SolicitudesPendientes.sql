CREATE VIEW [dbo].[vw_SolicitudesPendientes]
AS
    SELECT
        [Codigo],
        [ClienteId],
        [Monto],
        [Moneda],
        [Estado],
        [FechaVencimiento]
    FROM [dbo].[Solicitud]
    WHERE [Estado] IN (0, 1, 2);