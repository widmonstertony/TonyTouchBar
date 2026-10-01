CC ?= cc
CFLAGS ?= -O2 -Wall -Wextra -Werror -pthread
LDLIBS ?= -lusb-1.0 -pthread

.PHONY: all clean
all: t2-touchbar-bridge

t2-touchbar-bridge: t2-touchbar-bridge.c
	$(CC) $(CFLAGS) -o $@ $< $(LDLIBS)

clean:
	rm -f t2-touchbar-bridge
