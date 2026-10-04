CREATE PROCEDURE [dbo].[usp_Solicitud_MarcarVencidas]
    @FechaCorte DATETIME2(0),
    @FilasAfectadas INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    -- Filtra por estado no terminal ademas de por fecha. Sin eso, una solicitud
    -- ya anulada que luego pasara su fecha volveria a marcarse como vencida.
    UPDATE [dbo].[Solicitud]
       SET [Estado] = 4,
           [FechaModificacion] = @FechaCorte
     WHERE [Estado] IN (0, 1)
       AND [FechaVencimiento] < @FechaCorte;

    SET @FilasAfectadas = @@ROWCOUNT;
END