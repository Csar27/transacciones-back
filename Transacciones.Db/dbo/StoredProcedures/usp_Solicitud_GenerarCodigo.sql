CREATE PROCEDURE [dbo].[usp_Solicitud_GenerarCodigo]
    @Anio SMALLINT
AS
BEGIN
    SET NOCOUNT ON;

    -- Devuelve una FILA, no un parametro OUTPUT. Con Dapper, un OUTPUT obliga a
    -- declararlo aparte con DynamicParameters; devolver el resultado como fila
    -- se mapea directo y no tiene forma de olvidarse.
    --
    -- La SEQUENCE aporta el correlativo; el ano se antepone aparte. Meter el ano
    -- dentro del numero (SOL-2026-000001 -> 2026000001) no vale, porque al
    -- extraer el correlativo para incrementarlo habria que quitar el ano de en
    -- medio y es fragil.
    --
    -- El correlativo NO se reinicia por ano: es global y monotono. Si el negocio
    -- exige que reinicie cada enero, la solucion es una tabla de correlativos
    -- por ano actualizada con MERGE, no un "MAX + 1".
    SELECT CAST(
        CONCAT(
            'SOL-',
            FORMAT(@Anio, '0000'),
            '-',
            FORMAT(NEXT VALUE FOR [dbo].[SeqSolicitudCodigo], '000000'))
        AS NVARCHAR(30)) AS Codigo;
END