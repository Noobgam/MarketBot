import matplotlib.pyplot as plt


f = open('Bin/Release/CS/money.txt', 'r');
data = f.readlines()
x = []
y = []
i = 1
for line in data[:7 * 24 * 2]:
    splitted = line.split(';')
    x.append(i)
    i += 1
    y.append(splitted[1])

plt.plot(x, y)

plt.title('Money-money')
plt.grid(True)
plt.savefig("money.png")
plt.show()