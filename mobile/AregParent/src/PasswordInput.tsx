import { useState } from 'react';
import { Pressable, StyleSheet, Text, TextInput, View } from 'react-native';
import { t } from './i18n';
import { theme } from './theme';

type Props = {
  placeholder: string;
  value: string;
  onChangeText: (v: string) => void;
  editable?: boolean;
  autoComplete?: 'current-password' | 'new-password';
};

/**
 * A password box with a show/hide control.
 *
 * Typing a password blind on a phone keyboard is where most failed sign-ins
 * come from, and without this the parent has no way to check what they typed.
 */
export default function PasswordInput({
  placeholder,
  value,
  onChangeText,
  editable = true,
  autoComplete,
}: Props) {
  const [shown, setShown] = useState(false);
  const label = shown ? t('hide_password') : t('show_password');
  return (
    <View style={styles.wrap}>
      <TextInput
        style={styles.input}
        placeholder={placeholder}
        secureTextEntry={!shown}
        value={value}
        onChangeText={onChangeText}
        editable={editable}
        autoCapitalize="none"
        autoCorrect={false}
        autoComplete={autoComplete}
      />
      <Pressable
        style={styles.eye}
        onPress={() => setShown((s) => !s)}
        accessibilityRole="button"
        accessibilityLabel={label}
        accessibilityState={{ selected: shown }}
        hitSlop={8}
      >
        <Text style={styles.eyeText}>{shown ? '🙈' : '👁'}</Text>
      </Pressable>
    </View>
  );
}

const styles = StyleSheet.create({
  wrap: { position: 'relative', marginBottom: 12 },
  input: {
    borderWidth: 1,
    borderColor: theme.lineInput,
    borderRadius: 8,
    padding: 12,
    paddingRight: 52,
    fontSize: 16,
  },
  eye: {
    position: 'absolute',
    right: 2,
    top: 2,
    bottom: 2,
    width: 46,
    alignItems: 'center',
    justifyContent: 'center',
  },
  eyeText: { fontSize: 18 },
});
