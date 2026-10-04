-- Tipo definido por el usuario para el codigo de estado.
--
-- Existe para que los codigos no se escriban como cadenas sueltas por ahi
-- ("Reg", "RECH", "rechazada"...). Con un tipo, el compilador avisa.
CREATE TYPE [dbo].[CodigoEstado] FROM NVARCHAR(20) NOT NULL;