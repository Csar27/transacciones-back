-- Correlativo de codigos de solicitud.
--
-- Se usa una SEQUENCE y no "MAX(Codigo) + 1" por un motivo concreto: MAX + 1
-- tiene una condicion de carrera. Dos peticiones concurrentes leen el mismo
-- maximo y generan el mismo codigo; el indice unico rejecta la segunda, y el
-- usuario recibe un error en una operacion que era valida.
--
-- La SEQUENCE es atomica: el motor garantiza valores distintos sin bloqueos.
CREATE SEQUENCE [dbo].[SeqSolicitudCodigo]
    AS BIGINT
    START WITH 1
    INCREMENT BY 1
    MINVALUE 1
    NO MAXVALUE
    CACHE 20;
GO