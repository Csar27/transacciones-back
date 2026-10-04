CREATE PROCEDURE [dbo].[usp_Solicitud_ObtenerPorId]
    @Id UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        [Id],
        [Codigo],
        [ClienteId],
        [Monto],
        [Moneda],
        [Estado],
        [Observacion],
        [MotivoRechazo],
        [FechaSolicitud],
        [FechaVencimiento],
        [FechaAprobacion],
        [FechaRechazo],
        [FechaCreacion],
        [CreadoPor]
    FROM [dbo].[Solicitud]
    WHERE [Id] = @Id;
END